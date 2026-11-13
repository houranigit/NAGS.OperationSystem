package com.nags.operations.data

/** Each discovered feed owns its own bounded membership; neither grants Scheduled assignment. */
enum class FlightDiscoverySource(val key: String) { Search("search"), History("history") }
