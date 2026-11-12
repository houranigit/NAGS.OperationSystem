package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** Inset grouped headings follow the form's quiet iPhone Settings hierarchy. */
@Composable
internal fun FlightFormSectionTitle(title: String, modifier: Modifier = Modifier) {
    Text(title, modifier.padding(start = 16.dp, end = 16.dp).semantics { heading() },
        style = MaterialTheme.typography.titleSmall.copy(
            fontSize = if (LocalFlightFormCompact.current) 12.sp else 15.sp,
            lineHeight = if (LocalFlightFormCompact.current) 16.sp else 20.sp),
        fontWeight = FontWeight.Bold, color = Color(0xFF85858A))
}
