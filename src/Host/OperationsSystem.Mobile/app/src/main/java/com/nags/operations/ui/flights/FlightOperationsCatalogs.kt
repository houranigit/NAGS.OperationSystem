package com.nags.operations.ui.flights

import com.nags.operations.data.AtaChapterCatalogItem
import com.nags.operations.data.ResourceCatalogItem
import com.nags.operations.data.ServiceCatalogItem
import com.nags.operations.data.StaffCatalogItem

/** Existing cached reference choices used by the operation editors. */
data class FlightOperationsCatalogs(
    val services: List<ServiceCatalogItem> = emptyList(),
    val ataChapters: List<AtaChapterCatalogItem> = emptyList(),
    val stationStaff: List<StaffCatalogItem> = emptyList(),
    val tools: List<ResourceCatalogItem> = emptyList(),
    val materials: List<ResourceCatalogItem> = emptyList(),
    val generalSupports: List<ResourceCatalogItem> = emptyList(),
)

/** Choices follow the current flight station; historical saved attribution stays on its record. */
internal fun FlightOperationsCatalogs.forStation(stationId: String): FlightOperationsCatalogs = copy(
    stationStaff = stationStaff.filter { stationId.isNotBlank() && it.stationId.equals(stationId, true) },
)
