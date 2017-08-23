#!/usr/bin/env bash
set -ex
if [ ! -z "$1" ]; then
    COMMIT=$1
else
    COMMIT=`cat protocol_version.txt`
fi

rm -Rf tmp
mkdir tmp
cd tmp
git clone https://github.com/Internet-of-People/iop-message-protocol.git
cd iop-message-protocol
git checkout $COMMIT
protoc --csharp_out=../.. *.proto
cd ../..

