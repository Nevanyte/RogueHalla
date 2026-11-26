#!/usr/bin/env bash
# Upload one committed, native age v1 capture. A successful exit confirms S3's checksum.
set -euo pipefail

if (( $# != 4 )); then
  echo 'usage: upload-capture.sh <bucket> <12-digit-bucket-owner> <staging|live> <absolute-capture-path>' >&2
  exit 2
fi

bucket=$1
owner=$2
environment=$3
artifact=$4
name=${artifact##*/}
profile=${AWS_PROFILE:?Set AWS_PROFILE to the approved capture role profile}
if ! [[ $bucket =~ ^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$ &&
        $owner =~ ^[0-9]{12}$ &&
        ( $environment == staging || $environment == live ) &&
        $artifact == /* && -f $artifact && ! -L $artifact &&
        ( $name =~ ^replay-[0-9a-f]{32}[.]json[.]age$ ||
          $name =~ ^metrics-[0-9a-f]{32}-[0-9]+[.]json[.]age$ ) ]]; then
  echo 'invalid bucket, environment or committed .json.age capture path' >&2
  exit 2
fi

# Confirm the native age format and X25519 stanza before any AWS command.
if ! {
  IFS= read -r -n 80 format && [[ $format == age-encryption.org/v1 ]] &&
  IFS= read -r -n 80 stanza && [[ $stanza =~ ^'-> X25519 '[A-Za-z0-9+/]{43}$ ]];
} < "$artifact"; then
  echo 'capture must contain the native age v1 X25519 envelope' >&2
  exit 2
fi

checksum=$(openssl dgst -sha256 -binary < "$artifact" | openssl base64 -A)
received=$(
  AWS_IGNORE_CONFIGURED_ENDPOINT_URLS=true aws --profile "$profile" s3api put-object \
    --no-cli-pager --region ca-west-1 --expected-bucket-owner "$owner" \
    --bucket "$bucket" --key "$environment/$name" --body "$artifact" \
    --if-none-match '*' --server-side-encryption AES256 \
    --content-type application/octet-stream --checksum-algorithm SHA256 \
    --checksum-sha256 "$checksum" --query ChecksumSHA256 --output text
)
if [[ $received != "$checksum" ]]; then
  echo 'S3 did not confirm the capture checksum. Keep the local copy for reconciliation.' >&2
  exit 1
fi
printf '%s %s\n' "$checksum" "$environment/$name"
