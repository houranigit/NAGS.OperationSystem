package com.nags.operations.data

import kotlinx.serialization.Serializable

/** Shared reference catalogs; the keys also match the host's catalog change hints. */
enum class SharedCatalog(val storageKey: String, val endpointPath: String) {
    AircraftTypes("aircraft-types", "api/v1/masterdata/aircraft-types/options"),
    Customers("customers", "api/v1/masterdata/customers/options"),
    Services("services", "api/v1/masterdata/services/options"),
    Tools("tools", "api/v1/masterdata/tools/options"),
    Materials("materials", "api/v1/masterdata/materials/options"),
    GeneralSupports("general-supports", "api/v1/masterdata/general-supports/options"),
    AtaChapters("ata-chapters", "api/v1/masterdata/ata-chapters/options"),
    StationStaff("staff-members", "api/v1/masterdata/staff-members/options"),
}

data class CatalogSyncStatus(
    val kind: SharedCatalog,
    val itemCount: Int = 0,
    val lastAttemptUtc: String? = null,
    val lastSuccessUtc: String? = null,
    val error: String? = null,
    val permissionDenied: Boolean = false,
    val lastDurationMs: Long? = null,
)

@Serializable
data class AircraftTypeCatalogItem(val id: String, val manufacturer: String, val model: String)

@Serializable
data class CustomerCatalogItem(val id: String, val iataCode: String? = null, val name: String, val logoVersion: String? = null)

@Serializable
data class ServiceCatalogItem(val id: String, val name: String, val isAircraftPerLanding: Boolean)

@Serializable
data class ResourceCatalogItem(val id: String, val name: String, val calculationType: String)

@Serializable
data class StaffCatalogItem(
    val id: String,
    val fullName: String,
    val employeeId: String,
    val stationId: String,
    val stationCode: String,
    val manpowerTypeName: String,
)

@Serializable
data class AtaChapterCatalogItem(
    val id: String,
    val categoryId: String,
    val categoryName: String,
    val code: String,
    val title: String,
)

/** Image payload, kept outside the JSON/Room flight and catalog snapshots. */
data class CustomerLogoDownload(val version: String, val bytes: ByteArray)
