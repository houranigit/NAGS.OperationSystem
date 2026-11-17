package com.nags.operations.ui.flights

import java.util.Locale
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class ExpandableShareMessageTest {
    @Test fun reservesSpaceForInlineSuffixAndTrimsTrailingWhitespace() {
        val result = collapsedSharePrefix("One two three four five", Locale.ENGLISH) {
            (it.trimEnd() + "… See more").length <= 23
        }
        assertEquals("One two three", result)
    }

    @Test fun truncationDoesNotSplitEmojiOrCombiningCharacters() {
        val emoji = collapsedSharePrefix("A😀B", Locale.ENGLISH) { it.length <= 2 }
        assertEquals("A", emoji)
        assertFalse(emoji.last().isHighSurrogate())
        assertEquals("", collapsedSharePrefix("a\u0301b", Locale.ENGLISH) { it.length <= 1 })
    }

    @Test fun emptyAndFullyFittingMessagesRemainIntact() {
        assertEquals("", collapsedSharePrefix("", Locale.ENGLISH) { true })
        assertEquals("مرحبا", collapsedSharePrefix("مرحبا", Locale.forLanguageTag("ar")) { true })
    }
}
