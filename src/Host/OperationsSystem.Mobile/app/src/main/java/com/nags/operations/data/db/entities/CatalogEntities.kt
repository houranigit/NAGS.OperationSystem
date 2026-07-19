package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.ColumnInfo
import androidx.room.PrimaryKey

/**
 * Catalog tables — small reference data the mobile UI uses to render pickers.
 * Each entity stores the server-side id as its primary key so syncs are idempotent
 * (re-inserting the same id from the server just overwrites the row).
 *
 * No `lastSyncedAt` column on the rows themselves; per-table sync metadata lives
 * in [SyncStateEntity] so a refresh that touches one table doesn't have to
 * rewrite every row's timestamp.
 */

@Entity(tableName = "services")
data class ServiceEntity(
    @PrimaryKey val serviceId: String,
    val name: String,
    /** The well-known Aircraft Per Landing designation — excluded from work-order pickers. */
    val isAircraftPerLanding: Boolean,
    /** Personalized allowance for the signed-in staff member's active manpower type. */
    @ColumnInfo(defaultValue = "0")
    val isAllowedPerformedService: Boolean = false,
)

fun ServiceEntity.isAllowedPerformedOption(): Boolean =
    isAllowedPerformedService && !isAircraftPerLanding

fun List<ServiceEntity>.allowedPerformedServiceOptions(): List<ServiceEntity> =
    filter(ServiceEntity::isAllowedPerformedOption)

fun Iterable<ServiceEntity>.allowedPerformedServiceIds(): Set<String> =
    asSequence()
        .filter(ServiceEntity::isAllowedPerformedOption)
        .mapTo(linkedSetOf(), ServiceEntity::serviceId)

@Entity(tableName = "tools")
data class ToolEntity(
    @PrimaryKey val toolId: String,
    val name: String,
)

@Entity(tableName = "materials")
data class MaterialEntity(
    @PrimaryKey val materialId: String,
    val name: String,
)

@Entity(tableName = "general_supports")
data class GeneralSupportEntity(
    @PrimaryKey val generalSupportId: String,
    val name: String,
)

@Entity(tableName = "customers")
data class CustomerEntity(
    @PrimaryKey val customerId: String,
    val iataCode: String?,
    val name: String,
)

/** Single-line label for service-line and task pickers. */
fun EmployeeEntity.workOrderPickerDisplayLine(): String = "$fullName · $employeeNumber"
