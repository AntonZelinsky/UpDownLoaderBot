class ErrorDownload(Exception):
    def __init__(self, error: str, retry_count=None):
        self.error = error
        self.retry_count = retry_count
