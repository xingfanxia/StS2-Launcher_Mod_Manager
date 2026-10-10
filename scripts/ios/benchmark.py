#!/usr/bin/env python3
"""Capture iPad metrics and summarize exact game frame intervals."""

import argparse
from datetime import datetime, timezone
import html
import json
import math
import os
from pathlib import Path
import signal
import statistics
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parents[2]


def records(path):
    result = []
    for line in path.read_text().splitlines():
        try:
            row = json.loads(line)
            if isinstance(row, dict):
                result.append(row)
        except json.JSONDecodeError:
            continue  # A live writer can leave the final line incomplete.
    return result


def percentile(values, quantile):
    return sorted(values)[max(0, math.ceil(quantile * len(values)) - 1)] if values else None


def frame_summary(batches):
    intervals = [value for batch in batches for value in batch.get("frame_us", []) if value > 0]
    if not intervals:
        return None
    elapsed = sum(intervals) / 1_000_000
    slowest = sorted(intervals, reverse=True)[:max(1, math.ceil(len(intervals) / 100))]
    return {
        "frames": len(intervals), "active_seconds": elapsed,
        "average_fps": len(intervals) / elapsed,
        "one_percent_low_fps": 1_000_000 / statistics.mean(slowest),
        "p50_ms": percentile(intervals, 0.50) / 1000,
        "p95_ms": percentile(intervals, 0.95) / 1000,
        "p99_ms": percentile(intervals, 0.99) / 1000,
        "worst_ms": max(intervals) / 1000,
        "stalls_over_50ms": sum(value > 50_000 for value in intervals),
        "stalls_over_100ms": sum(value > 100_000 for value in intervals),
    }


def metrics(rows, key):
    values = [row[key] for row in rows if isinstance(row.get(key), (int, float))]
    if not values:
        return None
    return {"samples": len(values), "median": statistics.median(values),
            "p95": percentile(values, .95), "maximum": max(values), "minimum": min(values)}


def timestamp(row):
    if "_host_utc_ms" in row:
        return row["_host_utc_ms"]
    if "timestamp" in row:
        return datetime.fromisoformat(row["timestamp"]).timestamp() * 1000
    return row.get("utc_ms")


def summarize(frame_records, process_rows=(), graphics_rows=(), start_ms=None, end_ms=None):
    def in_range(row):
        stamp = timestamp(row)
        return stamp is not None and (start_ms is None or stamp >= start_ms) and (end_ms is None or stamp <= end_ms)

    batches = [r for r in frame_records if r.get("event") == "frames" and in_range(r)]
    # Include only complete sampling windows within the requested capture interval.
    if start_ms is not None:
        batches = [r for r in batches if r["utc_ms"] - sum(r["frame_us"]) / 1000 >= start_ms]
    process_rows = [r for r in process_rows if in_range(r)]
    graphics_rows = [r for r in graphics_rows if in_range(r)]
    return {
        "schema": 1,
        "window": {"start_utc_ms": start_ms, "end_utc_ms": end_ms},
        "runtime": next((r for r in frame_records if r.get("event") == "start"), None),
        "all_active": frame_summary(batches),
        "by_scene": {scene: frame_summary([r for r in batches if r.get("scene") == scene])
                     for scene in sorted({r.get("scene", "unknown") for r in batches})},
        "process_cpu_percent_of_one_core": metrics(process_rows, "cpuUsage"),
        "process_footprint_bytes": metrics(process_rows, "physFootprint"),
        "device_gpu_utilization_percent": metrics(graphics_rows, "Device Utilization %"),
        "device_compositor_fps": metrics(graphics_rows, "CoreAnimationFramesPerSecond"),
        "process_ids": sorted({r["pid"] for r in process_rows if "pid" in r}),
        "suspension_events": sum(r.get("event") == "background" and in_range(r) for r in frame_records),
        "notes": [
            "Frame intervals are SceneTree process cadence, not GPU completion/presentation timestamps.",
            "1% low FPS is 1e6 divided by the mean of the slowest 1% of frame intervals.",
            "Background time is excluded by the app lifecycle recorder; real foreground stalls remain.",
            "GPU and compositor counters cover the whole device; keep StS2 foreground during capture.",
            "CPU percent uses one core as 100%; this is not percent of total device CPU capacity.",
            "Run includes combat, map, rewards and menus within a run; mixed includes scene transitions.",
            "Complete five-second batches inside the host capture interval are used; edge batches are omitted.",
            "This is warm-cache, user-driven gameplay, not a controlled scene replay or cold-cache benchmark.",
        ],
    }, batches


def write_report(output, summary, batches):
    output.mkdir(parents=True, exist_ok=True)
    (output / "report.json").write_text(json.dumps(summary, indent=2) + "\n")
    lines = ["# iPad gameplay benchmark", "", "| Scene | Active seconds | Average FPS | 1% low FPS | p95 / p99 ms | Worst ms | >50 ms |",
             "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
    for scene, values in {"All active": summary["all_active"], **summary["by_scene"]}.items():
        if values:
            lines.append(f"| {scene} | {values['active_seconds']:.1f} | {values['average_fps']:.2f} | "
                         f"{values['one_percent_low_fps']:.2f} | {values['p95_ms']:.2f} / {values['p99_ms']:.2f} | "
                         f"{values['worst_ms']:.2f} | {values['stalls_over_50ms']} |")
    lines += ["", "| Counter | Median | p95 | Peak |", "| --- | ---: | ---: | ---: |"]
    for key, name, scale in (
        ("process_cpu_percent_of_one_core", "App CPU (% of one core)", 1),
        ("process_footprint_bytes", "App memory footprint (MiB)", 1 / 1048576),
        ("device_gpu_utilization_percent", "Device GPU (%)", 1),
        ("device_compositor_fps", "Device compositor FPS", 1),
    ):
        value = summary[key]
        if value:
            lines.append(f"| {name} | {value['median'] * scale:.2f} | {value['p95'] * scale:.2f} | {value['maximum'] * scale:.2f} |")
    lines += ["", *["- " + note for note in summary["notes"]]]
    (output / "report.md").write_text("\n".join(lines) + "\n")

    # Standalone SVG: tooltips reveal exact sample values without third-party scripts.
    width, height, left, top = 900, 270, 60, 35
    points = []
    circles = []
    for index, batch in enumerate(batches):
        value = frame_summary([batch])
        x = left + index * (width - left - 20) / max(1, len(batches) - 1)
        y = top + (1 - min(value["average_fps"], 65) / 65) * 190
        points.append(f"{x:.1f},{y:.1f}")
        tooltip = html.escape(f"{batch.get('scene')}: {value['average_fps']:.2f} FPS; p99 {value['p99_ms']:.2f} ms; worst {value['worst_ms']:.2f} ms")
        circles.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="4"><title>{tooltip}</title></circle>')
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" role="img" aria-label="Average FPS per sampling window">
<rect width="100%" height="100%" fill="#fff"/>
<g font-family="sans-serif" font-size="14" fill="#243746"><text x="60" y="20">Average FPS per sampling window (hover points for frame times)</text>
<text x="60" y="260">Sampling windows in capture order; background time excluded</text>'''
    for fps in (0, 30, 60):
        y = top + (1 - fps / 65) * 190
        svg += f'<text x="22" y="{y + 5:.1f}">{fps}</text><path d="M60 {y:.1f} H880" stroke="#ddd"/>'
    svg += '</g><polyline fill="none" stroke="#146ba1" stroke-width="2" points="' + " ".join(points) + '"/>'
    svg += '<g fill="#146ba1">' + "".join(circles) + '</g></svg>'
    (output / "fps.svg").write_text(svg)
    print("\n".join(lines[:12]))
    print(f"Report: {output / 'report.md'}")


def stream_metrics(command, path, errors, env, process_list):
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=errors, text=True,
                               env=env, start_new_session=True)
    process_list.append(process)
    def drain():
        with path.open("w") as output:
            for line in process.stdout:
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if not isinstance(row, dict):
                    continue
                row["_host_utc_ms"] = int(time.time() * 1000)
                output.write(json.dumps(row) + "\n")
                output.flush()
    reader = threading.Thread(target=drain, daemon=True)
    reader.start()
    return process, reader


def capture(args):
    cfg = json.loads((ROOT / "ios/config.local.json").read_text())
    output = args.output or ROOT / ".cache/benchmarks" / datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    output.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ, PYMOBILEDEVICE3_UDID=args.udid)
    prefix = ["uvx", "--from", str(args.pymobiledevice3), "pymobiledevice3", "developer", "dvt"]
    processes, readers = [], []
    start = int(time.time() * 1000)
    with (output / "transport.log").open("w") as errors:
        try:
            for name, command in (
                ("graphics", prefix + ["graphics"]),
                ("process", prefix + ["sysmon", "process", "monitor", "process", "--filter", "name=StS2",
                    "--choose", "first", "--keep-monitoring", "--duration", str(args.seconds * 1000),
                    "--key", "cpuUsage", "--key", "physFootprint", "--key", "pid",
                    "--output", str(output / "process.jsonl")]),
            ):
                # Sysmon already writes timestamped JSONL; its stdout contains status text.
                destination = output / ("graphics.jsonl" if name == "graphics" else "process-status.jsonl")
                _, reader = stream_metrics(command, destination, errors, env, processes)
                readers.append(reader)
            print(f"Capturing for {args.seconds}s. Keep StS2 foreground and play normally.", flush=True)
            deadline = time.monotonic() + args.seconds
            while time.monotonic() < deadline:
                if all(process.poll() is not None for process in processes):
                    raise RuntimeError(f"Metric services stopped; see {output / 'transport.log'}")
                time.sleep(min(1, max(0, deadline - time.monotonic())))
        finally:
            end = int(time.time() * 1000)
            for process in processes:
                if process.poll() is None:
                    # uvx can have a child CLI; stop the owned profiler group, not the game.
                    try:
                        os.killpg(process.pid, signal.SIGTERM)
                    except ProcessLookupError:
                        pass
            for process in processes:
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait()
            for reader in readers:
                reader.join(timeout=5)
    (output / "capture.json").write_text(json.dumps({"start_utc_ms": start, "end_utc_ms": end}, indent=2))
    print("Capture complete; collecting game frame data.", flush=True)
    subprocess.run(["xcrun", "devicectl", "device", "copy", "from", "--device", cfg["device"],
                    "--source", "Documents/benchmarks", "--destination", str(output / "frames"),
                    "--domain-type", "appDataContainer", "--domain-identifier", cfg["bundle_id"]], check=True)
    frame_records = []
    for path in sorted((output / "frames").rglob("*.jsonl")):
        frame_records.extend(records(path))
    # Prefer runtime metadata for the session overlapping this capture.
    starts = [r for r in frame_records if r.get("event") == "start" and r["utc_ms"] <= end]
    if starts:
        frame_records = [max(starts, key=lambda r: r["utc_ms"])] + [r for r in frame_records if r.get("event") != "start"]
    summary, batches = summarize(frame_records, records(output / "process.jsonl"),
                                 records(output / "graphics.jsonl"), start, end)
    write_report(output, summary, batches)
    if summary["all_active"] is None:
        raise SystemExit("No game frame samples in this capture. Enable profile in config.local.json and rebuild.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    live = commands.add_parser("capture")
    live.add_argument("--seconds", type=int, default=120)
    live.add_argument("--udid", required=True, help="Physical iPad UDID, not CoreDevice UUID")
    live.add_argument("--pymobiledevice3", type=Path, required=True, help="Local pymobiledevice3 checkout")
    live.add_argument("--output", type=Path)
    report = commands.add_parser("report")
    report.add_argument("frames", type=Path)
    report.add_argument("--process", type=Path)
    report.add_argument("--graphics", type=Path)
    report.add_argument("--window", type=Path, help="capture.json limits the report to the original capture interval")
    report.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "capture":
        if not 10 <= args.seconds <= 1800:
            parser.error("--seconds must be between 10 and 1800")
        capture(args)
    else:
        window = json.loads(args.window.read_text()) if args.window else {}
        summary, batches = summarize(records(args.frames), records(args.process) if args.process else (),
                                     records(args.graphics) if args.graphics else (),
                                     window.get("start_utc_ms"), window.get("end_utc_ms"))
        write_report(args.output, summary, batches)


if __name__ == "__main__":
    main()
