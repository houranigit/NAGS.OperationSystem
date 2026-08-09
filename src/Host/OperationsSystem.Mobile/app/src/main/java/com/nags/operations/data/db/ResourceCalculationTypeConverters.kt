package com.nags.operations.data.db

import androidx.room.TypeConverter
import com.nags.operations.data.ResourceCalculationType

/** Persists the stable API enum names in the offline catalog cache. */
class ResourceCalculationTypeConverters {
    @TypeConverter
    fun toStorage(value: ResourceCalculationType): String = value.name

    @TypeConverter
    fun fromStorage(value: String): ResourceCalculationType =
        ResourceCalculationType.valueOf(value)
}
