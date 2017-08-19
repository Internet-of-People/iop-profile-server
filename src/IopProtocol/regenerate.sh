#!/usr/bin/env bash

rm -R tmp
mkdir tmp
cd tmp
wget https://github.com/Internet-of-People/message-protocol/archive/master.zip
unzip master.zip
cd message-protocol-master
protoc --csharp_out=../.. *.proto
cd ../..

