"""Writes a plain 256x256 PNG for the Workshop preview."""
import struct
import sys
import zlib


def chunk(kind, data):
    body = kind + data
    return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))


size = 256
row = b"\x00" + bytes((40, 20, 60)) * size
png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)) \
    + chunk(b"IDAT", zlib.compress(row * size)) + chunk(b"IEND", b"")
open(sys.argv[1], "wb").write(png)
