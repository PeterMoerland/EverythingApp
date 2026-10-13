"""Startpunt voor PyInstaller (en `python start.py` tijdens ontwikkelen)."""
import multiprocessing
import os
import sys

# Vóór numpy/ingeniamotion geladen wordt: OpenBLAS op één thread, anders kan het laden op pc's met
# veel kernen of een krap wisselbestand mislukken ("Memory allocation still failed").
for _naam in ("OPENBLAS_NUM_THREADS", "OMP_NUM_THREADS", "MKL_NUM_THREADS"):
    os.environ.setdefault(_naam, "1")

from mde_agent.__main__ import main  # noqa: E402

if __name__ == "__main__":
    multiprocessing.freeze_support()
    sys.exit(main())
