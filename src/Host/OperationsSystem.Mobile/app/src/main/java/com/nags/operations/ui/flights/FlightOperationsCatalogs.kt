package com.nags.operations.ui.flights

import com.nags.operations.data.AtaChapterCatalogItem
import com.nags.operations.data.ResourceCatalogItem
import com.nags.operations.data.ServiceCatalogItem
import com.nags.operations.data.StaffCatalogItem

/** Existing cached reference choices used while editing temporary operation entries. */
data class FlightOperationsCatalogs(
    val services: List<ServiceCatalogItem> = emptyList(),
    val ataChapters: List<AtaChapterCatalogItem> = emptyList(),
    val stationStaff: List<StaffCatalogItem> = emptyList(),
    val tools: List<ResourceCatalogItem> = emptyList(),
    val materials: List<ResourceCatalogItem> = emptyList(),
    val generalSupports: List<ResourceCatalogItem> = emptyList(),
)
