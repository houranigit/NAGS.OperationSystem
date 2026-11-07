package com.nags.operations.ui.components

import org.junit.Assert.assertEquals
import org.junit.Test

class InitialsAvatarStackTest {
    @Test
    fun staffAndServiceNamesUseFirstAndLastWord() {
        assertEquals("AH", avatarInitials("Ahmed Al Hourani"))
        assertEquals("PW", avatarInitials("Potable Water"))
        assertEquals("RF", avatarInitials("Ramp & Fuel"))
        assertEquals("أم", avatarInitials("أحمد محمود"))
    }

    @Test
    fun singleWordsUseTwoLettersWithoutDiacriticsBecomingAnInitial() {
        assertEquals("MA", avatarInitials("Marshalling"))
        assertEquals("ÉL", avatarInitials("e\u0301lodie"))
        assertEquals("مح", avatarInitials("محمد"))
        assertEquals("A", avatarInitials("A"))
    }

    @Test
    fun separatorsAndEmptyNamesHaveSafeFallbacks() {
        assertEquals("JD", avatarInitials("  Jane\u00A0Doe  "))
        assertEquals("2S", avatarInitials("24 Hour Support"))
        assertEquals("?", avatarInitials("  "))
        assertEquals("?", avatarInitials("--- & …"))
    }

    @Test
    fun supplementaryLettersRemainWholeCodePoints() {
        val first = String(Character.toChars(0x10428))
        val upper = String(Character.toChars(0x10400))
        assertEquals(upper + "D", avatarInitials(first + "name Doe"))
        assertEquals(upper + "A", avatarInitials(first + "a"))
    }
}
