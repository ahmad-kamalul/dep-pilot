"""Minimal AWS Signature V4 client for a real MinIO object-persistence smoke test."""
from datetime import datetime, timezone
import hashlib
import hmac
from urllib import request


class S3Probe:
    def __init__(self, endpoint, access_key, secret_key):
        self.endpoint = endpoint.rstrip("/")
        self.access_key = access_key
        self.secret_key = secret_key

    def call(self, method, path, body=b""):
        now = datetime.now(timezone.utc)
        stamp, day = now.strftime("%Y%m%dT%H%M%SZ"), now.strftime("%Y%m%d")
        host = self.endpoint.split("://", 1)[1]
        payload_hash = hashlib.sha256(body).hexdigest()
        signed_headers = "host;x-amz-content-sha256;x-amz-date"
        canonical_headers = f"host:{host}\nx-amz-content-sha256:{payload_hash}\nx-amz-date:{stamp}\n"
        canonical_request = "\n".join((method, path, "", canonical_headers, signed_headers, payload_hash))
        scope = f"{day}/us-east-1/s3/aws4_request"
        string_to_sign = "\n".join(("AWS4-HMAC-SHA256", stamp, scope, hashlib.sha256(canonical_request.encode()).hexdigest()))
        signing_key = ("AWS4" + self.secret_key).encode()
        for component in (day, "us-east-1", "s3", "aws4_request"):
            signing_key = hmac.new(signing_key, component.encode(), hashlib.sha256).digest()
        signature = hmac.new(signing_key, string_to_sign.encode(), hashlib.sha256).hexdigest()
        headers = {
            "Host": host,
            "x-amz-date": stamp,
            "x-amz-content-sha256": payload_hash,
            "Authorization": f"AWS4-HMAC-SHA256 Credential={self.access_key}/{scope}, SignedHeaders={signed_headers}, Signature={signature}",
        }
        data = body if method == "PUT" else None
        with request.urlopen(request.Request(self.endpoint + path, data=data, headers=headers, method=method), timeout=20) as response:
            return response.read()
