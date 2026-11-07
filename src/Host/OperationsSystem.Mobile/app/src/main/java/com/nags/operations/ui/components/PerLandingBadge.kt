package com.nags.operations.ui.components

import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R

/** Shared compact designation for flight cards and the details toolbar. */
@Composable
internal fun PerLandingBadge(modifier: Modifier = Modifier) {
    Surface(modifier.widthIn(max = 72.dp), shape = RoundedCornerShape(4.dp),
        color = Color(0xFFFFF1DE), contentColor = Color(0xFF9A4500)) {
        Text(stringResource(R.string.flights_per_landing), Modifier.padding(horizontal = 5.dp, vertical = 2.dp),
            style = MaterialTheme.typography.labelSmall.copy(fontSize = 10.sp, lineHeight = 12.sp, letterSpacing = 0.1.sp),
            fontWeight = FontWeight.SemiBold, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
    }
}
