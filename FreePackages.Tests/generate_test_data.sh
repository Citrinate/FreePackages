#!/bin/bash

ASF_SERVER=${1:-"http://localhost"}
ASF_PORT=${2:-"1242"}
ASF_PASSWORD=${3:-""}
URL="$ASF_SERVER:$ASF_PORT/Api/FreePackages/ASF/GetProductInfo"
OUTPUT_DIR="TestData"

mkdir -p $OUTPUT_DIR

function get_app {
    if [[ $# -lt 2 ]]; then
        echo "Not enough arguments to get_app"
        exit 0
        fi
    
    curl -G $URL \
        -d appIDs=$1 \
        -d returnFirstRaw=true \
        -H "Authentication: $ASF_PASSWORD" \
        -o "$OUTPUT_DIR/$2.txt"
}

function get_sub {
    if [[ $# -lt 2 ]]; then
        echo "Not enough arguments to get_sub"
        exit 0
        fi

    curl -G $URL \
        -d packageIDs=$1 \
        -d returnFirstRaw=true \
        -H "Authentication: $ASF_PASSWORD" \
        -o "$OUTPUT_DIR/$2.txt"
}

# For filter testing

get_app 440 "app_with_type"

get_app 440 "app_with_tags"
get_app 410 "demo_with_fewer_tags_than_parent"
get_app 400 "demo_with_fewer_tags_than_parent_parent"

get_app 440 "app_with_categories"
get_app 2385860 "playtest_with_no_categories"
get_app 1778820 "playtest_with_no_categories_parent"
get_app 410 "demo_with_fewer_categories_than_parent"
get_app 400 "demo_with_fewer_categories_than_parent_parent"

get_app 440 "app_with_language_support"
get_app 2385860 "playtest_with_no_languages"
get_app 1778820 "playtest_with_no_languages_parent"
get_app 1316010 "demo_with_fewer_languages_than_parent"
get_app 962130 "demo_with_fewer_languages_than_parent_parent"

get_app 440 "app_with_review_score"

get_app 440 "app_with_content_descriptors"
get_app 547490 "demo_with_fewer_content_descriptors_than_parent"
get_app 418240 "demo_with_fewer_content_descriptors_than_parent_parent"

get_app 2437370 "playtest_with_no_waitlist"
get_app 1873120 "playtest_with_no_waitlist_parent"

get_sub 81948 "package_with_free_weekend"

get_sub 907539 "package_with_single_app"
get_app 1086940 "package_with_single_app_app_1"

get_sub 44911 "package_which_is_no_cost"

# For app testing

get_app 440 "app_which_is_free"

get_app 1086940 "app_with_release_state"

get_app 440 "app_with_state"

get_app 2378500 "app_with_required_app"

get_app 1245610 "app_with_restricted_countries"

get_app 212200 "app_with_purchase_restricted_countries"

get_app 34330 "app_with_dlc"

get_app 2423370 "playtest_with_hidden_parent"
get_app 2423350 "playtest_with_hidden_parent_parent"

get_app 1086940 "app_with_deck_verified"
get_app 30 "app_with_deck_playable"
get_app 43160 "app_with_deck_unsupported"
get_app 1449570 "app_with_deck_unknown"

# For package testing

get_sub 953346 "package_which_is_free"

get_sub 20737 "package_with_deactivated_demo"

get_sub 20737 "package_with_timed_activation"

get_sub 657460 "package_with_disallowed_app"

get_sub 178 "package_with_restricted_countries"

get_sub 1890 "package_with_purchase_restricted_countries"
