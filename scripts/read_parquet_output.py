"""Read parquet output from the data engineering agent.

Usage:
    python scripts/read_parquet_output.py

Requires:
    - .env file with STORAGE_ACCOUNT_NAME
    - Azure CLI login for authentication
"""

import os
import sys
from pathlib import Path
import pandas as pd
import io
from dotenv import load_dotenv

# Add src to path for imports
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "src-python"))

# Load environment variables
env_file = Path(__file__).resolve().parent.parent / ".env"
load_dotenv(env_file)

# Set ADLS_ACCOUNT_NAME for the ADLS client
if "STORAGE_ACCOUNT_NAME" in os.environ and "ADLS_ACCOUNT_NAME" not in os.environ:
    os.environ["ADLS_ACCOUNT_NAME"] = os.environ["STORAGE_ACCOUNT_NAME"]

from clients.adls import list_files, download_file

def convert_to_excel(client_id: str = "CLIENT_001"):
    """Convert all parquet outputs to Excel file."""
    
    storage_account = os.environ.get("STORAGE_ACCOUNT_NAME")
    if not storage_account:
        print("Error: STORAGE_ACCOUNT_NAME not found in environment")
        return None
    
    try:
        print(f"Looking for output files in container 'output' for client: {client_id}")
        
        # List all folders under the client
        output_files = list_files("output", f"{client_id}/")
        
        if not output_files:
            print(f"No output files found for client {client_id}")
            return None
        
        # Find parquet files and group by timestamp
        parquet_files = [f for f in output_files if f.endswith('.parquet')]
        
        if not parquet_files:
            print("No parquet files found in output")
            return None
        
        print(f"Found {len(parquet_files)} parquet files")
        
        # Group files by timestamp (folder)
        timestamp_groups = {}
        for file in parquet_files:
            # Extract timestamp from path: CLIENT_001/20260303_014909/part-...
            parts = file.split('/')
            if len(parts) >= 2:
                timestamp = parts[1]
                if timestamp not in timestamp_groups:
                    timestamp_groups[timestamp] = []
                timestamp_groups[timestamp].append(file)
        
        excel_filename = f"output/{client_id}_transformed_data.xlsx"
        
        # Create Excel writer
        with pd.ExcelWriter(excel_filename, engine='openpyxl') as writer:
            
            for timestamp, files in timestamp_groups.items():
                print(f"\nProcessing {timestamp} - {len(files)} files...")
                
                # Read all parquet files for this timestamp
                dfs = []
                for file in files:
                    print(f"  Reading: {file.split('/')[-1]}")
                    parquet_data = download_file("output", file)
                    df = pd.read_parquet(io.BytesIO(parquet_data))
                    dfs.append(df)
                
                # Combine all parts
                if dfs:
                    combined_df = pd.concat(dfs, ignore_index=True)
                    
                    # Create sheet name (Excel sheet names have max 31 chars)
                    sheet_name = f"{timestamp}"[:31]
                    
                    # Write to Excel sheet
                    combined_df.to_excel(writer, sheet_name=sheet_name, index=False)
                    
                    print(f"  ✅ Written to sheet '{sheet_name}': {len(combined_df)} rows, {len(combined_df.columns)} columns")
        
        print(f"\n🎉 Excel file created: {excel_filename}")
        
        # Also create a summary
        print(f"\nSummary:")
        for timestamp, files in timestamp_groups.items():
            print(f"  {timestamp}: {len(files)} parquet files")
        
        return excel_filename
        
    except Exception as e:
        print(f"Error converting to Excel: {e}")
        import traceback
        traceback.print_exc()
        return None


def read_latest_output(client_id: str = "CLIENT_001"):
    """Read the most recent parquet output for a client."""
    
    storage_account = os.environ.get("STORAGE_ACCOUNT_NAME")
    if not storage_account:
        print("Error: STORAGE_ACCOUNT_NAME not found in environment")
        return None
    
    try:
        print(f"Looking for output files in container 'output' for client: {client_id}")
        
        # List all folders under the client
        output_files = list_files("output", f"{client_id}/")
        
        if not output_files:
            print(f"No output files found for client {client_id}")
            return None
        
        # Find parquet files
        parquet_files = [f for f in output_files if f.endswith('.parquet')]
        
        if not parquet_files:
            print("No parquet files found in output")
            print(f"Available files: {output_files}")
            return None
        
        print(f"Found {len(parquet_files)} parquet files:")
        for f in parquet_files:
            print(f"  - {f}")
        
        # Read the first parquet file as an example
        first_parquet = parquet_files[0]
        print(f"\nReading: {first_parquet}")
        
        parquet_data = download_file("output", first_parquet)
        
        # Read parquet from bytes
        df = pd.read_parquet(io.BytesIO(parquet_data))
        
        print(f"Successfully loaded {len(df)} rows from {first_parquet}")
        print("\nDataFrame Info:")
        print(df.info())
        
        print(f"\nFirst 5 rows:")
        print(df.head())
        
        if len(parquet_files) > 1:
            print(f"\nNote: This shows data from {first_parquet} only.")
            print(f"To read all {len(parquet_files)} files, combine them manually.")
        
        return df
        
    except Exception as e:
        print(f"Error reading parquet file: {e}")
        print("Make sure you have:")
        print("1. Azure CLI logged in (az login)")
        print("2. Correct storage account name in .env")
        print("3. Output files exist in ADLS")
        return None

if __name__ == "__main__":
    print("Converting parquet files to Excel...")
    excel_file = convert_to_excel()
    
    if excel_file:
        print(f"\n✅ Conversion complete! Excel file saved as: {excel_file}")
    else:
        print("\n❌ Conversion failed.")