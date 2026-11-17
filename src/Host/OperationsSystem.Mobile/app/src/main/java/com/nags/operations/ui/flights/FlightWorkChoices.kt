package com.nags.operations.ui.flights

import java.text.Collator
import java.text.Normalizer
import java.util.Locale

internal data class FlightWorkChoice(
    val id: String,
    val title: String,
    val subtitle: String = "",
    val groupKey: String? = null,
    val groupTitle: String? = null,
    val searchText: String = "",
    val avatarName: String? = null,
)

internal data class FlightWorkChoiceGroup(val key: String, val title: String, val choices: List<FlightWorkChoice>)

/** Alphabetic indexes use the displayed full title, preserving Unicode letters rather than inferring a surname. */
internal fun flightPickerIndexLetter(value: String, locale: Locale): String {
    val normalized = Normalizer.normalize(value.trim(), Normalizer.Form.NFC)
    val first = normalized.takeIf { it.isNotEmpty() }?.codePointAt(0)
    return if (first != null && Character.isLetter(first)) String(Character.toChars(first)).uppercase(locale) else "#"
}

/** Presentation grouping only; eligibility and station scope belong to the caller's catalog choices. */
internal fun flightWorkChoiceGroups(options: List<FlightWorkChoice>, search: String,
    locale: Locale = Locale.getDefault(),
): List<FlightWorkChoiceGroup> {
    val query = search.trim()
    val collator = Collator.getInstance(locale).apply { strength = Collator.PRIMARY }
    val choiceComparator = Comparator<FlightWorkChoice> { first, second ->
        collator.compare(first.title.trim(), second.title.trim()).takeIf { it != 0 } ?: first.id.compareTo(second.id)
    }
    val filtered = options.filter { choice ->
        query.isEmpty() || listOf(choice.title, choice.subtitle, choice.searchText, choice.groupTitle.orEmpty())
            .any { it.contains(query, ignoreCase = true) }
    }
    val groups = filtered.groupBy { choice ->
        choice.groupKey?.takeIf { it.isNotBlank() }?.let { "group:$it" }
            ?: "letter:${flightPickerIndexLetter(choice.title, locale)}"
    }.map { (key, choices) ->
        val title = if (key.startsWith("group:")) choices.first().groupTitle?.trim()?.takeIf { it.isNotEmpty() }
            ?: choices.first().groupKey.orEmpty() else key.removePrefix("letter:")
        FlightWorkChoiceGroup(key, title, choices.sortedWith(choiceComparator))
    }
    return groups.sortedWith { first, second ->
        when {
            first.title == "#" && second.title != "#" -> 1
            first.title != "#" && second.title == "#" -> -1
            else -> collator.compare(first.title, second.title).takeIf { it != 0 } ?: first.key.compareTo(second.key)
        }
    }
}
