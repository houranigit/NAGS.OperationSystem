package com.nags.operations.ui.theme

import androidx.compose.material3.LocalRippleConfiguration
import androidx.compose.material3.ColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance

internal val ColorScheme.isDark: Boolean
    get() = background.luminance() < 0.5f

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
    primary = Color(0xFFF3AAA9),
    onPrimary = Color(0xFF2A0606),
    primaryContainer = Color(0xFF432121),
    onPrimaryContainer = Color(0xFFFFD8D8),
    inversePrimary = BrandRed,
    secondary = Color(0xFFCCCCCC),
    onSecondary = Color(0xFF222222),
    secondaryContainer = Color(0xFF303030),
    onSecondaryContainer = Color(0xFFF0F0F0),
    tertiary = Color(0xFF8FB6FF),
    onTertiary = Color(0xFF0D2852),
    tertiaryContainer = Color(0xFF21334B),
    onTertiaryContainer = Color(0xFFD9E5FF),
    background = Color.Black,
    onBackground = Color(0xFFF2F2F2),
    surface = Color(0xFF171717),
    onSurface = Color(0xFFF2F2F2),
    surfaceVariant = Color(0xFF252525),
    onSurfaceVariant = Color(0xFFBCBCBC),
    surfaceTint = Color(0xFFBCBCBC),
    surfaceDim = Color(0xFF101010),
    surfaceBright = Color(0xFF3B3B3B),
    surfaceContainerLowest = Color(0xFF0B0B0B),
    surfaceContainerLow = Color(0xFF141414),
    surfaceContainer = Color(0xFF1C1C1C),
    surfaceContainerHigh = Color(0xFF252525),
    surfaceContainerHighest = Color(0xFF303030),
    inverseSurface = Color(0xFFE9E9E9),
    inverseOnSurface = Color(0xFF292929),
    outline = Color(0xFF888888),
    outlineVariant = Color(0xFF3A3A3A),
    error = Color(0xFFFFB4B4),
    onError = Color(0xFF410000),
    errorContainer = Color(0xFF541F24),
    onErrorContainer = Color(0xFFFFDAD9),
)

@Composable
fun OperationsTheme(
    darkTheme: Boolean = false,
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        typography = Typography,
    ) {
        // Clickable flight form field rows opt into ripple at their own surface only.
        CompositionLocalProvider(LocalRippleConfiguration provides null, content = content)
    }
}
