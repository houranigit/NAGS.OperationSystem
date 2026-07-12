package com.nags.operations.data.db

import androidx.room.TypeConverter
import com.nags.operations.data.db.entities.FlightAssignedEmployeeSummary
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/**
 * Room type converter for the per-flight assigned-employees list on `flights_my`. Persisted
 * as a JSON string column (same rationale as `FlightServiceConverters`): the list is
 * read-only on device, every sync replaces the whole flight row, and the UI scans the
 * in-memory list rather than filtering in SQL.
 *
 * See the note in `FlightServiceConverters` on why the reified `encodeToString` /
 * `decodeFromString` extensions are used instead of an explicit `.serializer()` call.
 */
class FlightAssignedEmployeeConverters {
    @TypeConverter
    fun listToJson(list: List<FlightAssignedEmployeeSummary>): String =
        json.encodeToString(list)

    @TypeConverter
    fun jsonToList(payload: String?): List<FlightAssignedEmployeeSummary> {
        if (payload.isNullOrBlank()) return emptyList()
        return runCatching { json.decodeFromString<List<FlightAssignedEmployeeSummary>>(payload) }
            .getOrElse { emptyList() }
    }

    companion object {
        private val json = Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
        }
    }
}
