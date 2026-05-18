"""JinGu save decryptor.
AES from System.Security.Cryptography with default (CBC, PKCS7, 256-bit).
Key+IV from Rfc2898DeriveBytes("MyPassword", salt, 1000) — PBKDF2-HMAC-SHA1.
"""
import sys, hashlib, struct
from pathlib import Path

SALT = bytes([2, 35, 126, 122, 156, 125, 98, 150])
PASSWORD = b"MyPassword"
ITERATIONS = 1000

def pbkdf2(out_len):
    return hashlib.pbkdf2_hmac("sha1", PASSWORD, SALT, ITERATIONS, dklen=out_len)

# .NET Aes.Create() defaults: KeySize=256, BlockSize=128 → need 32+16 = 48 bytes
KEY_IV = pbkdf2(48)
KEY, IV = KEY_IV[:32], KEY_IV[32:]

def aes_cbc_decrypt(ct):
    try:
        from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
        from cryptography.hazmat.primitives.padding import PKCS7
    except ImportError:
        sys.exit("pip install cryptography")
    dec = Cipher(algorithms.AES(KEY), modes.CBC(IV)).decryptor()
    raw = dec.update(ct) + dec.finalize()
    unp = PKCS7(128).unpadder()
    return unp.update(raw) + unp.finalize()

def aes_cbc_encrypt(pt):
    from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
    from cryptography.hazmat.primitives.padding import PKCS7
    p = PKCS7(128).padder()
    padded = p.update(pt) + p.finalize()
    enc = Cipher(algorithms.AES(KEY), modes.CBC(IV)).encryptor()
    return enc.update(padded) + enc.finalize()

if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "decrypt":
        data = Path(sys.argv[2]).read_bytes()
        out = aes_cbc_decrypt(data)
        Path(sys.argv[3]).write_bytes(out)
        print(f"decrypted {len(data)} → {len(out)} bytes, head: {out[:120]!r}")
    elif cmd == "encrypt":
        data = Path(sys.argv[2]).read_bytes()
        out = aes_cbc_encrypt(data)
        Path(sys.argv[3]).write_bytes(out)
        print(f"encrypted {len(data)} → {len(out)} bytes")
