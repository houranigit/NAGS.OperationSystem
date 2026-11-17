package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.unit.dp
import com.nags.operations.R

/** Keeps the author, message and inset flight geometry visible while posts load. */
@Composable
internal fun FlightShareSkeleton() {
    val description = stringResource(R.string.flights_skeleton_loading)
    val placeholder = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.08f)
    Column(Modifier.clearAndSetSemantics { contentDescription = description }) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Box(Modifier.size(32.dp).background(placeholder, CircleShape))
            Column(Modifier.weight(1f)) {
                Box(Modifier.fillMaxWidth(0.65f).height(16.dp)
                    .background(placeholder, RoundedCornerShape(4.dp)))
                Box(Modifier.padding(top = 2.dp).fillMaxWidth(0.5f).height(11.dp)
                    .background(placeholder, RoundedCornerShape(4.dp)))
                Box(Modifier.padding(top = 8.dp, bottom = 4.dp).fillMaxWidth(0.9f).height(19.dp)
                    .background(placeholder, RoundedCornerShape(4.dp)))
                FlightCardSkeleton()
            }
        }
        HorizontalDivider(Modifier.padding(top = 10.dp), thickness = 1.dp,
            color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.1f))
    }
}
