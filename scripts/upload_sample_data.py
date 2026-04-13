"""Upload input_data/ files to ADLS containers for testing.

Usage:
    python scripts/upload_sample_data.py

Requires:
    - .env file with STORAGE_ACCOUNT_NAME
    - Azure CLI login (az login) for DefaultAzureCredential
"""

import os
import sys
from pathlib import Path
from dotenv import load_dotenv

# Add src to path for imports
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "src-python"))

# Load environment variables from .env file
env_file = Path(__file__).resolve().parent.parent / ".env"
load_dotenv(env_file)

# Set ADLS_ACCOUNT_NAME for the ADLS client
if "STORAGE_ACCOUNT_NAME" in os.environ and "ADLS_ACCOUNT_NAME" not in os.environ:
    os.environ["ADLS_ACCOUNT_NAME"] = os.environ["STORAGE_ACCOUNT_NAME"]

from clients.adls import upload_file

INPUT_DIR = Path(__file__).resolve().parent.parent / "input_data"

# Map local files to ADLS containers/paths
UPLOADS = [
    {
        "container": "mappings",
        "local_file": "JG Copy of DNAV Data Dictionary.xlsm",
        "remote_path": "CLIENT_001/mapping.xlsm",
    },
    {
        "container": "data",
        "local_file": "Sample Data - effective transactions.xlsx",
        "remote_path": "CLIENT_001/transactions.xlsx",
    },
]


def main():
    if not INPUT_DIR.exists():
        print(f"Error: input_data/ directory not found at {INPUT_DIR}")
        sys.exit(1)

    for upload in UPLOADS:
        local_path = INPUT_DIR / upload["local_file"]
        if not local_path.exists():
            print(f"  SKIP: {upload['local_file']} (not found)")
            continue

        print(f"  Uploading {upload['local_file']} -> {upload['container']}/{upload['remote_path']}")
        data = local_path.read_bytes()
        upload_file(upload["container"], upload["remote_path"], data)
        print(f"  OK ({len(data):,} bytes)")

    print("\nDone.")


if __name__ == "__main__":
    main()

