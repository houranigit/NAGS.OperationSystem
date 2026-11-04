package com.nags.operations.data.sync

import com.nags.operations.data.SharedCatalog

/** Read-only datasets refreshed by the shared live channel and full-refresh controls. */
sealed interface SyncTarget {
    val storageKey: String

    data class Catalog(val catalog: SharedCatalog) : SyncTarget {
        override val storageKey: String get() = catalog.storageKey
    }

    data object Flights : SyncTarget {
        override val storageKey: String = "flights"
    }

    companion object {
        val all: Set<SyncTarget> = SharedCatalog.entries.mapTo(mutableSetOf<SyncTarget>()) { Catalog(it) } + Flights

        fun fromTable(table: String?): SyncTarget? = when (table) {
            Flights.storageKey -> Flights
            else -> SharedCatalog.entries.firstOrNull { it.storageKey == table }?.let(::Catalog)
        }
    }
}
