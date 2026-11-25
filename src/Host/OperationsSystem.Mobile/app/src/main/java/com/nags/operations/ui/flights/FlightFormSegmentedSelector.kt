package com.nags.operations.ui.flights

import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

@Composable
internal fun <T> FlightFormSegmentedSelector(value: T, options: List<Pair<T, String>>, width: Dp, enabled: Boolean, onChange: (T) -> Unit) {
    val style = MaterialTheme.typography.bodyMedium
    val faceHeight = maxOf(32.dp, with(LocalDensity.current) { style.lineHeight.toDp() } + 8.dp)
    BoxWithConstraints(Modifier.width(width).height(maxOf(48.dp, faceHeight))) {
        val segmentWidth = (maxWidth - 4.dp) / options.size
        val selected = options.indexOfFirst { it.first == value }
        val slide by animateDpAsState(segmentWidth * selected.coerceAtLeast(0),
            animationSpec = tween(180), label = "flightFormSelection")
        Box(Modifier.fillMaxWidth().height(faceHeight).align(Alignment.Center)
            .background(FlightFormGround, RoundedCornerShape(8.dp)).padding(2.dp)) {
            if (selected >= 0) Box(Modifier.offset(x = slide).width(segmentWidth).fillMaxHeight()
                .shadow(1.dp, RoundedCornerShape(6.dp)).background(MaterialTheme.colorScheme.surface, RoundedCornerShape(6.dp)))
        }
        Row(Modifier.fillMaxSize().padding(horizontal = 2.dp).selectableGroup()) {
            options.forEachIndexed { index, (option, title) ->
                Box(Modifier.weight(1f).fillMaxHeight().flightFormValidationFocus(enabled && index == 0)
                    .selectable(selected = option == value, enabled = enabled,
                        role = Role.RadioButton, onClick = { if (option != value) onChange(option) }), contentAlignment = Alignment.Center) {
                    Text(title, style = style, fontWeight = if (option == value) FontWeight.SemiBold else FontWeight.Medium,
                        color = if (option == value) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
        }
    }
}
