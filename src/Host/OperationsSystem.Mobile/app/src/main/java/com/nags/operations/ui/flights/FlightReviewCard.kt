package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.nags.operations.R

@Composable
internal fun FlightReviewOnlineHeading(cached: Boolean) {
    Column(Modifier.padding(horizontal = 14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(stringResource(R.string.flight_review_online), style = MaterialTheme.typography.titleSmall)
        if (cached) Text(stringResource(R.string.flight_review_offline),
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/** Overwrite only changes the editor. The existing Save remains the publishing boundary. */
@Composable
internal fun FlightReviewLocalCard(
    applied: Boolean,
    enabled: Boolean,
    onDiscard: () -> Unit,
    onOverwrite: () -> Unit,
    overwriteEnabled: Boolean = enabled,
    content: @Composable ColumnScope.() -> Unit,
) {
    Surface(Modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surface) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(stringResource(R.string.flight_review_local), style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold)
            content()
            Text(stringResource(if (applied) R.string.flight_review_applied else R.string.flight_review_merge_hint),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                TextButton(enabled = enabled, onClick = onDiscard) {
                    Text(stringResource(R.string.flight_review_discard), color = if (enabled) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant)
                }
                TextButton(enabled = enabled && overwriteEnabled, onClick = onOverwrite) { Text(stringResource(R.string.flight_review_overwrite)) }
            }
            TextButton(enabled = false, onClick = {}) { Text(stringResource(R.string.flight_review_submit_anyway)) }
        }
    }
}

@Composable
internal fun FlightReviewValue(label: String, value: String?) {
    Column(Modifier.fillMaxWidth()) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(value?.takeIf { it.isNotBlank() } ?: stringResource(R.string.flight_review_not_provided),
            style = MaterialTheme.typography.bodySmall)
    }
}
