package com.nags.operations.ui.theme

import android.content.Context
import androidx.compose.runtime.staticCompositionLocalOf
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow

/** A device preference; changing appearance never touches account or offline-work storage. */
enum class AppThemeMode { System, Light, Dark }

class AppAppearancePreferences(context: Context) {
    private val preferences = context.applicationContext.getSharedPreferences("app_appearance", Context.MODE_PRIVATE)
    private val _mode = MutableStateFlow(AppThemeMode.entries.firstOrNull {
        it.name == preferences.getString("theme_mode", null)
    } ?: AppThemeMode.System)
    val mode = _mode.asStateFlow()

    fun setMode(value: AppThemeMode) {
        if (value == _mode.value) return
        preferences.edit().putString("theme_mode", value.name).apply()
        _mode.value = value
    }
}

data class AppAppearance(val mode: AppThemeMode, val onChange: (AppThemeMode) -> Unit)
val LocalAppAppearance = staticCompositionLocalOf { AppAppearance(AppThemeMode.System) {} }
