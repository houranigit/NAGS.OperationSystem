package com.nags.operations.ui.theme

import androidx.compose.ui.text.font.DeviceFontFamilyName
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight

/** EMUI 12 on Android 10 resolves HarmonyOS semibold to the medium instance. */
internal fun operationsSansFontFamily(manufacturer: String, sdkInt: Int, hasHarmonyOsFont: Boolean): FontFamily =
    if (manufacturer.equals("HUAWEI", ignoreCase = true) && sdkInt == 29 && hasHarmonyOsFont) HarmonyOsSans
    else FontFamily.SansSerif

private val HarmonyOsSans = FontFamily((100..900 step 100).flatMap { value ->
    val weight = FontWeight(value)
    listOf(FontStyle.Normal, FontStyle.Italic).map { style ->
        Font(DeviceFontFamilyName("sans-serif"), weight, style,
            // Leave every other weight on the original OEM mapping, including its bold instance.
            variationSettings = if (weight == FontWeight.SemiBold) FontVariation.Settings(FontVariation.weight(600))
                else FontVariation.Settings())
    }
})
