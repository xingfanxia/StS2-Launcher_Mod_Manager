import unittest

from benchmark import frame_summary, summarize


class BenchmarkTests(unittest.TestCase):
    def test_fps_uses_elapsed_time_not_mean_of_window_rates(self):
        value = frame_summary([{"frame_us": [10_000] * 100}, {"frame_us": [100_000] * 10}])
        self.assertEqual(value["average_fps"], 55)
        self.assertEqual(value["one_percent_low_fps"], 10)
        self.assertEqual(value["p99_ms"], 100)

    def test_background_events_do_not_count_as_frames(self):
        rows = [{"event": "frames", "utc_ms": 1000, "scene": "run", "frame_us": [20_000] * 50},
                {"event": "background", "utc_ms": 2000}, {"event": "foreground", "utc_ms": 20000},
                {"event": "frames", "utc_ms": 21000, "scene": "run", "frame_us": [20_000] * 50}]
        summary, _ = summarize(rows)
        self.assertEqual(summary["all_active"]["active_seconds"], 2)
        self.assertEqual(summary["all_active"]["average_fps"], 50)
        self.assertEqual(summary["suspension_events"], 1)

    def test_capture_bounds_exclude_partial_batches_and_unrelated_metrics(self):
        rows = [{"event": "frames", "utc_ms": end, "scene": "run", "frame_us": [20_000] * 50}
                for end in (1000, 2000, 3000)]
        summary, _ = summarize(rows, [{"utc_ms": 500, "cpuUsage": 99}, {"utc_ms": 2000, "cpuUsage": 20}],
                               start_ms=1500, end_ms=3000)
        self.assertEqual(summary["all_active"]["frames"], 50)
        self.assertEqual(summary["process_cpu_percent_of_one_core"]["median"], 20)


if __name__ == "__main__":
    unittest.main()
