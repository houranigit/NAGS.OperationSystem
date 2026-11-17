package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.PlatformTextStyle
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLinkStyles
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.withLink
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import java.text.BreakIterator
import java.util.Locale

/** Measure the suffix too, so See more fits on the fourth line at the current font size. */
@Composable
internal fun ExpandableShareMessage(message: String) {
    var expanded by rememberSaveable(message) { mutableStateOf(false) }
    val more = stringResource(R.string.flight_shares_see_more)
    val locale = LocalLocale.current.platformLocale
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val style = MaterialTheme.typography.bodyMedium.copy(fontSize = 13.sp, lineHeight = 19.sp, fontWeight = FontWeight.Medium,
        platformStyle = PlatformTextStyle(includeFontPadding = false))
    val moreColor = MaterialTheme.colorScheme.onSurfaceVariant
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        val width = with(density) { maxWidth.roundToPx() }.coerceAtLeast(1)
        val displayed = remember(message, expanded, more, locale, style, width, measurer, moreColor) {
            fun candidate(prefix: String) = buildAnnotatedString {
                append(prefix.trimEnd())
                append("… ")
                withStyle(SpanStyle(fontWeight = FontWeight.Bold, color = moreColor)) {
                    withLink(LinkAnnotation.Clickable("expand-share", TextLinkStyles(
                        style = SpanStyle(fontWeight = FontWeight.Bold, color = moreColor))) { expanded = true }) {
                        append(more)
                    }
                }
            }
            val full = buildAnnotatedString { append(message) }
            fun fits(value: androidx.compose.ui.text.AnnotatedString) = !measurer.measure(value,
                style = style, constraints = Constraints(maxWidth = width), maxLines = 4).hasVisualOverflow
            if (expanded || fits(full)) full else {
                val prefix = collapsedSharePrefix(message, locale) { fits(candidate(it)) }
                candidate(prefix)
            }
        }
        Text(displayed, style = style, color = MaterialTheme.colorScheme.onSurface,
            maxLines = if (expanded) Int.MAX_VALUE else 4)
    }
}

/** Keep truncation on character boundaries, including surrogate pairs and combining characters. */
internal fun collapsedSharePrefix(message: String, locale: Locale, fits: (String) -> Boolean): String {
    val boundaries = mutableListOf(0)
    val iterator = BreakIterator.getCharacterInstance(locale).apply { setText(message) }
    var end = iterator.next()
    while (end != BreakIterator.DONE) { boundaries += end; end = iterator.next() }
    var low = 0
    var high = boundaries.lastIndex
    while (low < high) {
        val middle = (low + high + 1) / 2
        if (fits(message.substring(0, boundaries[middle]))) low = middle else high = middle - 1
    }
    return message.substring(0, boundaries[low]).trimEnd()
}
