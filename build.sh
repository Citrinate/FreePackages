#!/bin/bash

## https://github.com/Ryzhehvost/asf_plugin_creator

################################################################################

PATH=/bin:/sbin:/usr/bin:/usr/sbin:/usr/local/bin:/usr/local/sbin:$HOME/bin
export PATH

################################################################################

_PROGN_=`basename $0`

_INSTDIR_=`dirname $0`
[[ $_INSTDIR_ = . ]] && _INSTDIR_=`pwd`

################################################################################

## getting current directory name from '$_INSTDIR_' variable
plugin_name=$(echo $_INSTDIR_ | sed 's|.*/||')

# download submodule
if [[ ! -d ArchiSteamFarm/ArchiSteamFarm ]]; then
   git submodule update --init
fi

if [[ $# -gt 1 ]]; then
   echo "Too many arguments. Exiting."
   exit 1
elif [[ $# -eq 1 ]]; then
   ## update submodule to required tag as specified in '$1'
   git submodule foreach "git fetch origin; git checkout $1;"
else
   ## otherwise update submodule to latest tag
   git submodule foreach "git fetch origin; git checkout $(git rev-list --tags --max-count=1);"
fi

## print what version we are building for
git submodule foreach "git describe --tags;"

if [[ -d ./out ]]; then
   rm -rf ./out
fi

## release generic version
dotnet restore
sync
dotnet publish -c "Release" -f net8.0 -o "out/generic" "/p:LinkDuringPublish=false"
mkdir ./out/$plugin_name
cp ./out/generic/$plugin_name.dll ./out/$plugin_name
if [[ -f "README.md" ]]; then
   if ! command -v pandoc &> /dev/null; then
      cp README.md ./out/$plugin_name
   else
      pandoc --metadata title="$plugin_name" --standalone --columns 2000 -f markdown -t html --embed-resources --standalone -c ./github-pandoc.css -o ./out/$plugin_name/README.html README.md
   fi
fi
7z a -tzip -mx7 ./out/$plugin_name.zip ./out/$plugin_name
rm -rf out/$plugin_name
