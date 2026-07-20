package com.nags.operations.data

import com.nags.operations.data.api.WorkOrderServiceLineInput
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.decodeFromJsonElement
import kotlinx.serialization.json.encodeToJsonElement
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class MobileWorkOrderServiceLineCompatibilityTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test
    fun cached_line_decodes_multiple_performers() {
        val cached = json.decodeFromString<WorkOrderServiceLineWireDto>(
            """{
                "id":"line-1",
                "serviceId":"service-1",
                "serviceName":"Baggage",
                "performedBy":[
                  {"staffMemberId":"staff-1","fullName":"Staff One","employeeId":"E001"},
                  {"staffMemberId":"staff-2","fullName":"Staff Two","employeeId":"E002"}
                ],
                "performedByStaffMemberId":"legacy-first-alias",
                "performedByName":"Legacy First Alias",
                "fromUtc":"2026-07-11T10:00:00Z",
                "toUtc":"2026-07-11T11:00:00Z"
            }""".trimIndent(),
        )

        assertFalse(cached.isReturnToRamp)
        assertEquals(listOf("staff-1", "staff-2"), cached.performedBy.map { it.staffMemberId })
        assertEquals(listOf("Staff One", "Staff Two"), cached.performedBy.map { it.fullName })
        assertEquals(cached.performedBy, cached.effectivePerformedBy)
    }

    @Test
    fun legacy_direct_wire_decode_exposes_single_performer_as_effective_list() {
        val directNetworkResponse = json.decodeFromString<WorkOrderServiceLineWireDto>(
            """{
                "id":"line-1",
                "serviceId":"service-1",
                "serviceName":"Baggage",
                "performedByStaffMemberId":"staff-1",
                "performedByName":"Staff One",
                "fromUtc":"2026-07-11T10:00:00Z",
                "toUtc":"2026-07-11T11:00:00Z"
            }""".trimIndent(),
        )

        assertTrue(directNetworkResponse.performedBy.isEmpty())
        assertEquals(listOf("staff-1"), directNetworkResponse.effectivePerformedBy.map { it.staffMemberId })
        assertEquals("Staff One", directNetworkResponse.effectivePerformedBy.single().fullName)
    }

    @Test
    fun legacy_wire_performer_survives_cache_encoding_and_migration() {
        val cacheJson = Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
        }
        val directNetworkResponse = cacheJson.decodeFromString<WorkOrderServiceLineWireDto>(
            """{
                "id":"line-1",
                "serviceId":"service-1",
                "serviceName":"Baggage",
                "performedByStaffMemberId":"staff-1",
                "performedByName":"Staff One",
                "fromUtc":"2026-07-11T10:00:00Z",
                "toUtc":"2026-07-11T11:00:00Z"
            }""".trimIndent(),
        )
        val encodedLine = cacheJson.encodeToJsonElement(
            WorkOrderServiceLineWireDto.serializer(),
            directNetworkResponse,
        )
        val migratedWorkOrder = migrateLegacyWorkOrderServiceLinePerformers(
            buildJsonObject {
                put("serviceLines", buildJsonArray { add(encodedLine) })
            },
        )
        val decodedFromCache = cacheJson.decodeFromJsonElement<WorkOrderServiceLineWireDto>(
            migratedWorkOrder.jsonObject.getValue("serviceLines").jsonArray[0],
        )

        assertEquals(listOf("staff-1"), decodedFromCache.performedBy.map { it.staffMemberId })
        assertEquals("Staff One", decodedFromCache.performedBy.single().fullName)
    }

    @Test
    fun request_line_round_trips_multiple_performers_and_return_to_ramp_flag() {
        val request = WorkOrderServiceLineInput(
            id = "line-1",
            serviceId = "service-1",
            performedByStaffMemberIds = listOf("staff-1", "staff-2"),
            fromUtc = "2026-07-11T10:00:00Z",
            toUtc = "2026-07-11T11:00:00Z",
            isReturnToRamp = true,
        )
        val encoded = json.encodeToString(WorkOrderServiceLineInput.serializer(), request)

        val decoded = json.decodeFromString<WorkOrderServiceLineInput>(
            encoded,
        )

        assertTrue(decoded.isReturnToRamp)
        assertEquals("line-1", decoded.id)
        assertEquals(listOf("staff-1", "staff-2"), decoded.performedByStaffMemberIds)
        val keys = json.parseToJsonElement(encoded).jsonObject.keys
        assertTrue("performedByStaffMemberIds" in keys)
        assertFalse("performedByStaffMemberId" in keys)
    }

    @Test
    fun legacy_cached_line_migrates_single_performer_without_losing_it() {
        val legacy = json.parseToJsonElement(
            """{
                "id":"line-1",
                "serviceId":"service-1",
                "serviceName":"Baggage",
                "performedByStaffMemberId":"staff-1",
                "performedByName":"Staff One",
                "fromUtc":"2026-07-11T10:00:00Z",
                "toUtc":"2026-07-11T11:00:00Z"
            }""".trimIndent(),
        )

        val decoded = json.decodeFromJsonElement<WorkOrderServiceLineWireDto>(
            migrateLegacyWorkOrderServiceLinePerformers(
                buildJsonObject {
                    put("serviceLines", buildJsonArray { add(legacy) })
                },
            ).jsonObject.getValue("serviceLines").jsonArray[0],
        )

        assertEquals(listOf("staff-1"), decoded.performedBy.map { it.staffMemberId })
        assertEquals("Staff One", decoded.performedBy.single().fullName)
    }
}
