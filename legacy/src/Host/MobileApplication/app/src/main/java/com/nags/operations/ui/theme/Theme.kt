package com.nags.operations.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val LightColors = lightColorScheme(
    primary = BrandRed,
    onPrimary = Color.White,
    primaryContainer = BrandRedTint,
    onPrimaryContainer = BrandRedDark,
    secondary = BrandRedDark,
    onSecondary = Color.White,
    secondaryContainer = BrandRedTint,
    onSecondaryContainer = BrandRedDark,
    tertiary = AccentBlue,
    onTertiary = Color.White,
    background = SurfaceWarm,
    onBackground = OnSurfacePrimary,
    surface = Color.White,
    onSurface = OnSurfacePrimary,
    surfaceVariant = SurfaceMuted,
    onSurfaceVariant = OnSurfaceSecondary,
    outline = OutlineSoft,
    error = AccentRed,
    onError = Color.White,
)

private val DarkColors = darkColorScheme(
    primary = BrandRedLight,
    onPrimary = Color(0xFF2A0606),
    primaryContainer = BrandRedDark,
    onPrimaryContainer = Color(0xFFFFD8D8),
    secondary = BrandRedLight,
    onSecondary = Color(0xFF2A0606),
    tertiary = Color(0xFF8FB6FF),
    background = Color(0xFF1A0F11),
    onBackground = Color(0xFFF5E5E5),
    surface = Color(0xFF221619),
    onSurface = Color(0xFFF5E5E5),
    surfaceVariant = Color(0xFF382024),
    onSurfaceVariant = Color(0xFFE2C9CB),
    outline = Color(0xFF6B484C),
    error = Color(0xFFFFB4B4),
    onError = Color(0xFF410000),
)

@Composable
fun OperationsTheme(
    darkTheme: Boolean = false,
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        typography = Typography,
        content = content,
    )
}
