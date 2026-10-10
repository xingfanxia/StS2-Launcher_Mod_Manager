"""LLDB side of the StikDebug "universal" JIT protocol (brk #0xf00d).

Loaded into lldb by build.py launch. The app stops on `brk #0xf00d` with the
command in x16: 1 prepares the region x0/x1 by writing one byte into every
16 KiB page while attached, 0 asks the debugger to leave. The app checks every
page itself, so a failed write leaves the process stopped instead of
pretending success.
"""

import time

import lldb

BRK_MASK, BRK_OPCODE, IMMEDIATE = 0xFFE0001F, 0xD4200000, 0xF00D
PAGE = 0x4000
FILL = b"\x69"


def log(message):
    print("[JIT26] " + message, flush=True)


def bless(process, address, length):
    first = address & ~(PAGE - 1)
    pages = ((address + length + PAGE - 1) & ~(PAGE - 1)) - first
    pages //= PAGE
    started = time.monotonic()
    for index in range(pages):
        error = lldb.SBError()
        process.WriteMemory(first + index * PAGE, FILL, error)
        if not error.Success():
            log(f"write failed at page {index}/{pages}: {error.GetCString()}")
            return False
        if index and index % 2048 == 0:
            log(f"{index}/{pages} pages")
    log(f"prepared {pages} pages at 0x{first:x} in {time.monotonic() - started:.1f} s")
    return True


class Hook:
    def __init__(self, target, extra_args, internal_dict):
        pass

    def handle_stop(self, context, stream):
        process, frame = context.GetProcess(), context.GetFrame()
        if not frame.IsValid():
            return True
        pc = frame.GetPC()
        error = lldb.SBError()
        raw = process.ReadMemory(pc, 4, error)
        if not error.Success():
            return True
        instruction = int.from_bytes(raw, "little")
        if (instruction & BRK_MASK) != BRK_OPCODE or ((instruction >> 5) & 0xFFFF) != IMMEDIATE:
            return True
        command = frame.FindRegister("x16").GetValueAsUnsigned()
        if command == 1:
            address = frame.FindRegister("x0").GetValueAsUnsigned()
            length = frame.FindRegister("x1").GetValueAsUnsigned()
            if address == 0:
                # The app always maps its own region; debugger allocation is not offered.
                log("region request without an address is unsupported")
                return True
            if not bless(process, address, length):
                return True
            frame.SetPC(pc + 4)
            return False
        if command == 0:
            frame.SetPC(pc + 4)
            process.Detach()
            log("detached")
            return False
        log(f"unknown command {command}")
        return True


def __lldb_init_module(debugger, internal_dict):
    debugger.HandleCommand(f"target stop-hook add -P {__name__}.Hook")
