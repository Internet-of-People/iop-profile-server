#!/usr/bin/env bash

# This script regenerates the C# classes based on the iop-messsage-protocol
# definitions. You can use it simply as "./regenerate.sh" to reproduce the
# last integrated protocol version. The tag or commit hash for that is versioned
# together with the rest of the Profile Server sources in the file 
# protocol_version.txt next to this script.
#
# You can try another version by running:
#
#   $ ./regenerate.sh release/v1.0-beta2
#
# You can also test a protocol version that was not yet pushed to the
# integration repository by specifying it as a 2nd parameter:
#
#   $ ./regenerate.sh master ../../../iop-message-protocol/.git
#
# Make sure you do not commit generated C# sources without also updating the 
# protocol_version.txt!

set -ex
if [ ! -z "$1" ]; then
    COMMIT=$1
else
    COMMIT=$(cat protocol_version.txt)
fi
if [ ! -z "$2" ]; then
    REPO=$(realpath "$2")
else
    REPO=https://github.com/Internet-of-People/iop-message-protocol.git
fi

rm -Rf tmp
mkdir tmp
cd tmp
git clone "$REPO"
cd iop-message-protocol
git checkout "$COMMIT"
protoc --csharp_out=../.. --csharp_opt=file_extension=.g.cs ./*.proto
cd ../..