package com.nags.operations.ui.flights

import java.util.Locale
import org.junit.Assert.*
import org.junit.Test

class FlightWorkChoicesTest {
    @Test fun manufacturerGroupsRemainNamedAndSearchFindsAllTheirAircraft() {
        val options = listOf(
            FlightWorkChoice("boeing", "B777", groupKey = "Boeing", groupTitle = "Boeing"),
            FlightWorkChoice("airbus-later", "A350", groupKey = "Airbus", groupTitle = "Airbus"),
            FlightWorkChoice("airbus-first", "A320", groupKey = "Airbus", groupTitle = "Airbus"),
        )
        val groups = flightWorkChoiceGroups(options, "", Locale.ENGLISH)
        assertEquals(listOf("Airbus", "Boeing"), groups.map { it.title })
        assertEquals(listOf("airbus-first", "airbus-later"), groups.first().choices.map { it.id })
        assertEquals(groups.take(1), flightWorkChoiceGroups(options, " airBUS ", Locale.ENGLISH))
    }

    @Test fun searchIncludesSubtitleAndMetadataWithoutChangingTheCallerChoices() {
        val employee = FlightWorkChoice("staff", "Hassan Zaid", "Avionics technician", searchText = "E-42")
        val options = listOf(employee, FlightWorkChoice("other", "Omar Khalid", "Engineer"))
        listOf(" hassAN ", "AVIONICS", "e-42").forEach { query ->
            assertEquals(listOf(employee), flightWorkChoiceGroups(options, query, Locale.ENGLISH).flatMap { it.choices })
        }
        assertTrue(flightWorkChoiceGroups(options, "unmatched", Locale.ENGLISH).isEmpty())
        assertEquals(2, options.size)
    }

    @Test fun alphabeticalGroupingUsesFirstFullTitleAndStableChoiceIds() {
        val options = listOf(
            FlightWorkChoice("z", "alice Zaid"), FlightWorkChoice("a", " ALICE Ahmed"),
            FlightWorkChoice("b", "Bob Alice"), FlightWorkChoice("numeric", "123 Unknown"),
            FlightWorkChoice("empty", "  "),
        )
        val groups = flightWorkChoiceGroups(options, "", Locale.ENGLISH)
        assertEquals(listOf("A", "B", "#"), groups.map { it.title })
        assertEquals(listOf("a", "z"), groups.first().choices.map { it.id })
        assertEquals("b", groups[1].choices.single().id)
        val tied = flightWorkChoiceGroups(listOf(FlightWorkChoice("2", "Service"), FlightWorkChoice("1", "SERVICE")), "", Locale.ENGLISH)
        assertEquals(listOf("1", "2"), tied.single().choices.map { it.id })
    }

    @Test fun categoryIdentityIsNotCollapsedWhenDisplayNamesOrAlphabeticLabelsMatch() {
        val options = listOf(
            FlightWorkChoice("chapter-21", "21 · Air conditioning", groupKey = "category-a", groupTitle = "Systems"),
            FlightWorkChoice("chapter-05", "05 · Time limits", groupKey = "category-a", groupTitle = "Systems"),
            FlightWorkChoice("chapter-12", "12 · Servicing", groupKey = "category-b", groupTitle = "Systems"),
            FlightWorkChoice("named", "Systems support", groupKey = "S", groupTitle = "S"),
            FlightWorkChoice("plain", "Service"),
        )
        val groups = flightWorkChoiceGroups(options, "", Locale.ENGLISH)
        assertEquals(setOf("group:category-a", "group:category-b", "group:S", "letter:S"), groups.map { it.key }.toSet())
        assertEquals(listOf("chapter-05", "chapter-21"), groups.single { it.key == "group:category-a" }.choices.map { it.id })
        val categories = flightWorkChoiceGroups(options, "systems", Locale.ENGLISH)
        assertEquals(setOf("chapter-05", "chapter-12", "chapter-21", "named"), categories.flatMap { it.choices }.map { it.id }.toSet())
    }

    @Test fun unicodeSectionsPreserveCanonicalArabicAndSupplementaryLetters() {
        val options = listOf(
            FlightWorkChoice("accent", "Émile"), FlightWorkChoice("decomposed", "E\u0301lodie"),
            FlightWorkChoice("arabic", "  أحمد حسن"), FlightWorkChoice("supplementary", "𐐨lpha"),
            FlightWorkChoice("punctuation", "@Temporary"),
        )
        val groups = flightWorkChoiceGroups(options, "", Locale.ENGLISH)
        assertEquals(setOf("accent", "decomposed"), groups.single { it.title == "É" }.choices.map { it.id }.toSet())
        assertEquals("arabic", groups.single { it.title == "أ" }.choices.single().id)
        assertEquals("supplementary", groups.single { it.title == "𐐀" }.choices.single().id)
        assertEquals("#", groups.last().title)
        assertEquals("İ", flightPickerIndexLetter("istanbul", Locale.forLanguageTag("tr")))
    }
}
