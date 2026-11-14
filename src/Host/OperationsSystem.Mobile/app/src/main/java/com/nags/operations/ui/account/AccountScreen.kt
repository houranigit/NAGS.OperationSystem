package com.nags.operations.ui.account

import androidx.compose.runtime.getValue
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Sync
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.ui.components.AppProfileAvatar
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind

@Composable
fun AccountScreen(viewModel: AccountViewModel, onBack: () -> Unit, onLogout: () -> Unit,
    onOpenSyncCenter: () -> Unit, onOpenPassword: () -> Unit, cachedName: String? = null) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val context = LocalContext.current
    LifecycleResumeEffect(viewModel) { viewModel.refresh(); onPauseOrDispose { } }
    Scaffold(topBar = { ProfilePageHeader(stringResource(R.string.account_title), onBack) }) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp)) {
            Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)
                .padding(16.dp), verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                AppProfileAvatar(state.profile?.displayName ?: cachedName, size = 48.dp)
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(state.profile?.displayName ?: cachedName ?: stringResource(R.string.account_title),
                        style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                    state.profile?.email?.let { Text(it, style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant) }
                }
                if (state.isLoading) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
            }
            state.error?.let { InlineNotice(it, kind = InlineNoticeKind.Error,
                onRetry = if (state.isOnline && !state.isLoading) viewModel::refresh else null) }
            if (!state.isOnline) InlineNotice(stringResource(R.string.account_profile_offline))
            Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface)) {
                ProfileMenuRow(Icons.Outlined.Sync, stringResource(R.string.profile_sync_hub),
                    subtitle = stringResource(R.string.profile_sync_description), onClick = onOpenSyncCenter)
                HorizontalDivider(Modifier.padding(horizontal = 12.dp), color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
                ProfileMenuRow(Icons.Outlined.Lock, stringResource(R.string.profile_password), onClick = onOpenPassword)
                HorizontalDivider(Modifier.padding(horizontal = 12.dp), color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
                ProfileMenuRow(Icons.Outlined.Notifications, stringResource(R.string.profile_notifications),
                    onClick = { openNotificationPreferences(context) })
            }
            ProfileAppearanceSelector()
            ProfileMenuRow(Icons.AutoMirrored.Filled.Logout, stringResource(R.string.account_sign_out),
                destructive = true, onClick = onLogout)
        }
    }
}
