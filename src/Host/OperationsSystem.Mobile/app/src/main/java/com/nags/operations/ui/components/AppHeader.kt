package com.nags.operations.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.Add
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R

/** Compact home header; page tabs belong to the content below it. */
@Composable
fun AppHeader(
    displayName: String?,
    onProfileClick: () -> Unit,
    onCreateAdHoc: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val foreground = MaterialTheme.colorScheme.onSurface
    val muted = MaterialTheme.colorScheme.onSurfaceVariant
    val friendlyName = displayName?.trim()?.takeIf { it.isNotEmpty() }
        ?.split(Regex("\\s+"))?.firstOrNull()
    val greeting = if (friendlyName == null) stringResource(R.string.home_greeting_fallback)
        else stringResource(R.string.home_greeting, friendlyName)
    val createDescription = stringResource(R.string.home_create_adhoc)

    Column(modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surface).statusBarsPadding()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            IconButton(onClick = onProfileClick, modifier = Modifier.size(48.dp)) {
                Box(Modifier.size(36.dp).background(MaterialTheme.colorScheme.primaryContainer, CircleShape),
                    contentAlignment = Alignment.Center) {
                    Icon(Icons.Default.AccountCircle,
                        contentDescription = stringResource(R.string.account_open),
                        tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(26.dp))
                }
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(stringResource(R.string.home_welcome_back),
                    style = MaterialTheme.typography.bodySmall,
                    color = muted, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                Text(greeting,
                    style = MaterialTheme.typography.titleMedium,
                    color = foreground,
                    maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
            }
            Box(
                modifier = Modifier.heightIn(min = 48.dp)
                    .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null,
                        role = Role.Button, onClick = onCreateAdHoc)
                    .semantics { contentDescription = createDescription },
                contentAlignment = Alignment.Center,
            ) {
                Row(Modifier.heightIn(min = 34.dp)
                    .background(MaterialTheme.colorScheme.primary, RoundedCornerShape(12.dp))
                    .padding(horizontal = 10.dp, vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Default.Add, contentDescription = null,
                        tint = MaterialTheme.colorScheme.onPrimary, modifier = Modifier.size(16.dp))
                    Spacer(Modifier.width(4.dp))
                    Text(stringResource(R.string.home_adhoc), modifier = Modifier.clearAndSetSemantics {},
                        style = MaterialTheme.typography.labelLarge.copy(fontSize = 12.sp, fontWeight = FontWeight.SemiBold),
                        color = MaterialTheme.colorScheme.onPrimary, maxLines = 1, softWrap = false)
                }
            }
        }
    }
}
