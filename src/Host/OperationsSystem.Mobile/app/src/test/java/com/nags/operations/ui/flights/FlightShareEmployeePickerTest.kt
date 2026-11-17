package com.nags.operations.ui.flights

import com.nags.operations.data.StaffCatalogItem
import java.util.Locale
import org.junit.Assert.*
import org.junit.Test

class FlightShareEmployeePickerTest {
    @Test fun groupingUsesFirstFullNameLetterIgnoringLeadingWhitespaceAndCaseWithStableOrder() {
        val groups = flightShareEmployeeGroups(listOf(employee("2", " alice Zaid"), employee("1", "ALBERT Zaid"),
            employee("3", "Bob Ahmed")), "station", "", Locale.ENGLISH)
        assertEquals(listOf("A", "B"), groups.map { it.letter })
        assertEquals(listOf("1", "2"), groups.first().employees.map { it.id })
        // Surnames do not move Bob into the A section or Alice into Z.
        assertEquals("Bob Ahmed", groups.last().employees.single().fullName)
    }

    @Test fun nonLetterAndEmptyNamesUseLastHashSection() {
        val groups = flightShareEmployeeGroups(listOf(employee("digit", "123 Test"), employee("blank", "  "),
            employee("punct", "@Temp"), employee("name", "Zaid")), "station", "", Locale.ENGLISH)
        assertEquals(listOf("Z", "#"), groups.map { it.letter })
        assertEquals(setOf("digit", "blank", "punct"), groups.last().employees.map { it.id }.toSet())
    }

    @Test fun searchUsesNameEmployeeNumberAndManpowerOnlyInsideCurrentStation() {
        val mine = employee("one", "Hassan", employeeId = "E-42", manpower = "Avionics Technician")
        val other = employee("other", "Hassan", station = "other", employeeId = "E-42", manpower = "Avionics Technician")
        listOf(" hassAN ", "e-42", "AVIONICS").forEach { query ->
            val matches = flightShareEmployeeGroups(listOf(mine, other), "station", query, Locale.ENGLISH)
            assertEquals(listOf("one"), matches.flatMap { it.employees }.map { it.id })
        }
        assertTrue(flightShareEmployeeGroups(listOf(mine), "", "", Locale.ENGLISH).isEmpty())
        assertTrue(flightShareEmployeeGroups(listOf(mine), "station", "unmatched", Locale.ENGLISH).isEmpty())
    }

    @Test fun arabicNamesRemainArabicAndCanonicalUnicodeLettersGroupTogether() {
        val rows = listOf(employee("arabic", "  أحمد حسن"), employee("arabic2", "باسم علي"),
            employee("accent", "Émile"), employee("decomposed", "E\u0301lodie"))
        val groups = flightShareEmployeeGroups(rows, "station", "", Locale.forLanguageTag("ar"))
        assertTrue(groups.any { it.letter == "أ" && it.employees.single().id == "arabic" })
        assertTrue(groups.any { it.letter == "ب" && it.employees.single().id == "arabic2" })
        assertEquals(setOf("accent", "decomposed"), groups.single { it.letter == "É" }.employees.map { it.id }.toSet())
    }

    private fun employee(id: String, name: String, station: String = "station", employeeId: String = id,
        manpower: String = "Engineer") = StaffCatalogItem(id, name, employeeId, station, "RUH", manpower)
}
