package com.nags.operations.ui.theme

import androidx.compose.ui.text.font.AndroidFont
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontListFontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test

class HuaweiFontCompatibilityTest {
    @Test fun workingDevicesRetainTheirOriginalFontFamily() {
        assertSame(FontFamily.SansSerif, operationsSansFontFamily("Google", 36, false))
        assertSame(FontFamily.SansSerif, operationsSansFontFamily("IrisGuard_UK_Ltd", 27, false))
        assertSame(FontFamily.SansSerif, operationsSansFontFamily("Google", 29, true))
    }

    @Test fun workaroundRequiresTheObservedHuaweiFontStack() {
        assertSame(FontFamily.SansSerif, operationsSansFontFamily("HUAWEI", 29, false))
        assertSame(FontFamily.SansSerif, operationsSansFontFamily("HUAWEI", 28, true))
        assertSame(FontFamily.SansSerif, operationsSansFontFamily("HUAWEI", 30, true))
    }

    @Test fun onlySemiboldBypassesTheOemWeightMappingForBothStyles() {
        val fonts = operationsSansFontFamily("Huawei", 29, true) as FontListFontFamily
        assertEquals(listOf(FontStyle.Normal, FontStyle.Italic), fonts.filter { it.weight == FontWeight.SemiBold }.map { it.style })
        fonts.forEach { font ->
            assertEquals(if (font.weight == FontWeight.SemiBold) FontVariation.Settings(FontVariation.weight(600))
                else FontVariation.Settings(), (font as AndroidFont).variationSettings)
        }
    }
}
