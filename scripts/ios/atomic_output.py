"""Keep failed content patches from replacing the last usable pack."""

from contextlib import contextmanager
import os
from pathlib import Path
import tempfile


@contextmanager
def atomic_output(source, destination):
    source, destination = Path(source), Path(destination)
    if source.resolve() == destination.resolve() or (
        destination.exists() and os.path.samefile(source, destination)
    ):
        raise ValueError("Output must differ from the original content pack")
    destination.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=destination.name + ".", dir=destination.parent)
    os.close(fd)
    try:
        yield temporary
        os.replace(temporary, destination)
    finally:
        Path(temporary).unlink(missing_ok=True)
