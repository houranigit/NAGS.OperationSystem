package com.nags.operations.data

import com.nags.operations.data.sync.toServiceEntities
import com.nags.operations.data.db.entities.allowedPerformedServiceOptions
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MobileCatalogAllowanceTest {
    @Test
    fun catalogs_json_reads_allowed_performed_service_ids() {
        val decoded = Json.decodeFromString<MobileCatalogsDto>(
            """
            {
              "services": [{"id":"allowed","name":"Allowed"}],
              "allowedPerformedServiceIds": ["allowed"],
              "generatedAtUtc": "2026-07-18T10:00:00Z"
            }
            """.trimIndent(),
        )

        assertEquals(listOf("allowed"), decoded.allowedPerformedServiceIds)
    }

    @Test
    fun missing_allowance_property_fails_closed() {
        val decoded = Json.decodeFromString<MobileCatalogsDto>(
            """{"services":[],"generatedAtUtc":"2026-07-18T10:00:00Z"}""",
        )

        assertTrue(decoded.allowedPerformedServiceIds.isEmpty())
    }

    @Test
    fun mapping_keeps_full_catalog_but_marks_only_allowed_non_system_services() {
        val rows = MobileCatalogsDto(
            services = listOf(
                MobileServiceCatalogItemDto("allowed", "Allowed"),
                MobileServiceCatalogItemDto("blocked", "Blocked"),
                MobileServiceCatalogItemDto("per-landing", "Aircraft Per Landing", isAircraftPerLanding = true),
            ),
            allowedPerformedServiceIds = listOf("allowed", "per-landing"),
            generatedAtUtc = "2026-07-18T10:00:00Z",
        ).toServiceEntities()

        assertEquals(3, rows.size)
        assertTrue(rows.single { it.serviceId == "allowed" }.isAllowedPerformedService)
        assertFalse(rows.single { it.serviceId == "blocked" }.isAllowedPerformedService)
        assertFalse(rows.single { it.serviceId == "per-landing" }.isAllowedPerformedService)
        assertEquals(listOf("allowed"), rows.allowedPerformedServiceOptions().map { it.serviceId })
    }
}
