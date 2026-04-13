import os
import io
from azure.identity import DefaultAzureCredential
from azure.storage.filedatalake import DataLakeServiceClient


def get_adls_client() -> DataLakeServiceClient:
    account_name = os.environ["ADLS_ACCOUNT_NAME"]
    return DataLakeServiceClient(
        account_url=f"https://{account_name}.dfs.core.windows.net",
        credential=DefaultAzureCredential(),
    )


def download_file(container: str, path: str) -> bytes:
    client = get_adls_client()
    fs = client.get_file_system_client(container)
    file_client = fs.get_file_client(path)
    download = file_client.download_file()
    return download.readall()


def upload_file(container: str, path: str, data: bytes) -> None:
    client = get_adls_client()
    fs = client.get_file_system_client(container)
    file_client = fs.get_file_client(path)
    file_client.upload_data(data, overwrite=True, connection_timeout=300, read_timeout=300)


def list_files(container: str, prefix: str = "") -> list[str]:
    client = get_adls_client()
    fs = client.get_file_system_client(container)
    paths = fs.get_paths(path=prefix)
    return [p.name for p in paths]


def get_file_metadata(container: str, path: str) -> dict:
    """Get file properties including ETag."""
    client = get_adls_client()
    fs = client.get_file_system_client(container)
    file_client = fs.get_file_client(path)
    props = file_client.get_file_properties()
    return {"etag": props.etag, "size": props.size, "last_modified": str(props.last_modified)}

