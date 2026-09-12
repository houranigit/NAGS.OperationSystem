package com.nags.operations.data

/**
 * Stable MasterData identifiers mirrored from the server contract.
 *
 * Keep these values aligned with `MasterData.Contracts.Seeding.WellKnownMasterDataIds`.
 */
internal object WellKnownMasterDataIds {
    const val UnknownService = "40000000-0000-0000-0000-000000000003"
    const val UnknownTool = "60000000-0000-0000-0000-000000000001"
    const val UnknownMaterial = "70000000-0000-0000-0000-000000000001"
    const val UnknownGeneralSupport = "80000000-0000-0000-0000-000000000001"

    fun isUnknownResource(id: String): Boolean =
        listOf(UnknownTool, UnknownMaterial, UnknownGeneralSupport).any { it.equals(id, true) }

    const val UnknownCustomer = "50000000-0000-0000-0000-000000000001"
}
