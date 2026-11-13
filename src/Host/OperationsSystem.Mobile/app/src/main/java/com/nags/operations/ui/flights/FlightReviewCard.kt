package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLinkStyles
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withLink
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.ui.components.InlineNotice

@Composable
internal fun FlightReviewOnlineHeading(cached: Boolean) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(stringResource(R.string.flight_review_online), Modifier.padding(horizontal = 14.dp), style = MaterialTheme.typography.titleSmall)
        if (cached) InlineNotice(stringResource(R.string.flight_review_offline))
    }
}

/** The local editor is staged on entry. Only the form's explicit Save publishes it. */
@Composable
internal fun FlightReviewLocalHeading() {
    Column(Modifier.fillMaxWidth().padding(start = 14.dp, end = 14.dp, top = 16.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(stringResource(R.string.flight_review_local), style = MaterialTheme.typography.titleSmall,
            fontWeight = FontWeight.SemiBold)
        Text(stringResource(R.string.flight_review_edit_hint), style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
internal fun FlightReviewActions(enabled: Boolean, onDiscard: () -> Unit) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
        TextButton(enabled = enabled, onClick = onDiscard) {
            Text(stringResource(R.string.flight_review_discard),
                color = if (enabled) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/** Quiet retained-input explanation shared by each failed submitted section. */
@Composable
internal fun FlightFailedSubmissionNote(onReview: () -> Unit, modifier: Modifier = Modifier) {
    val message = stringResource(R.string.flight_review_failed_note)
    val review = stringResource(R.string.flight_review_action)
    val color = MaterialTheme.colorScheme.error
    Text(buildAnnotatedString {
        append(message)
        append(" ")
        withLink(LinkAnnotation.Clickable("review-submission", TextLinkStyles(style = SpanStyle(color = color))) { onReview() }) {
            append(review)
        }
    }, modifier.fillMaxWidth().padding(horizontal = 14.dp),
        style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 18.sp),
        color = MaterialTheme.colorScheme.onSurfaceVariant)
}
