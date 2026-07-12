package com.nags.operations.data.db

import androidx.room.TypeConverter
import com.nags.operations.data.db.entities.FlightServiceSummary
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/**
 * Room type converter for the per-flight services list. We persist the list as
 * a JSON string column on the flight tables rather than as
 * a child table because:
 *
 *  • the list is read-only on the device (mobile never edits a service),
 *  • every sync replaces the whole flight row anyway, so per-service
 *    incremental updates have no value,
 *  • the mobile UI never needs to filter flights by service in SQL — chip
 *    rendering and the seeding rule scan the in-memory list.
 *
 * Note on the reified extensions: KSP runs before the kotlinx-serialization
 * compiler plugin, so referencing `FlightServiceSummary.serializer()` here
 * would crash Room's KSP processor with a `MissingType` error. The reified
 * `encodeToString<T>` / `decodeFromString<T>` extensions sidestep that — they
 * resolve to the compiler-plugin output at code-gen time without requiring
 * the explicit `.serializer()` symbol in source.
 */
class FlightServiceConverters {
    @TypeConverter
    fun listToJson(list: List<FlightServiceSummary>): String =
        json.encodeToString(list)

    @TypeConverter
    fun jsonToList(payload: String?): List<FlightServiceSummary> {
        if (payload.isNullOrBlank()) return emptyList()
        return runCatching { json.decodeFromString<List<FlightServiceSummary>>(payload) }
            .getOrElse { emptyList() }
    }

    companion object {
        private val json = Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
        }
    }
}
