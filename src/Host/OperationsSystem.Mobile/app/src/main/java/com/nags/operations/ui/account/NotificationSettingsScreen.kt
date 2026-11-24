package com.nags.operations.ui.account

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ChevronRight
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.app.NotificationManagerCompat
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.R
import com.nags.operations.data.notifications.NotificationPreferenceKey
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind
import com.nags.operations.ui.flights.FlightFormGround
import com.nags.operations.ui.theme.LoadingIndicatorGray
import com.nags.operations.ui.theme.isDark

@Composable
fun NotificationSettingsScreen(viewModel: NotificationSettingsViewModel, onBack: () -> Unit) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val context = LocalContext.current
    var systemAllowed by remember { mutableStateOf(NotificationManagerCompat.from(context).areNotificationsEnabled()) }
    LifecycleResumeEffect(viewModel) {
        systemAllowed = NotificationManagerCompat.from(context).areNotificationsEnabled()
        viewModel.refresh()
        onPauseOrDispose { }
    }
    val ground = FlightFormGround
    val busyMessage = stringResource(if (state.savingKey != null) R.string.notification_settings_saving
        else R.string.notification_settings_loading)
    Scaffold(containerColor = ground,
        topBar = { ProfilePageHeader(stringResource(R.string.profile_notifications), onBack) {
            Box(Modifier.size(48.dp), contentAlignment = Alignment.Center) {
                if (state.isLoading || state.savingKey != null) CircularProgressIndicator(
                    Modifier.size(16.dp).semantics {
                        contentDescription = busyMessage
                        liveRegion = LiveRegionMode.Polite
                    }, strokeWidth = 2.dp, color = LoadingIndicatorGray)
            }
        } }) { padding ->
        Box(Modifier.fillMaxSize().padding(padding), contentAlignment = Alignment.TopCenter) {
            Column(Modifier.widthIn(max = 640.dp).fillMaxWidth().verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 20.dp), verticalArrangement = Arrangement.spacedBy(20.dp)) {
                if (!state.isOnline) InlineNotice(stringResource(R.string.notification_settings_offline))
                state.error?.let { message ->
                    InlineNotice(message, kind = InlineNoticeKind.Error,
                        onRetry = if (state.isOnline && !state.isLoading && state.savingKey == null) viewModel::refresh else null)
                }
                val values = state.preferences
                if (values != null) {
                    SettingsGroup {
                        NotificationSwitchRow(stringResource(R.string.notification_settings_allow), values.enabled,
                            state.canEdit, onChange = { viewModel.setPreference(NotificationPreferenceKey.Enabled, it) })
                    }
                    Text(stringResource(R.string.notification_settings_scope), Modifier.padding(horizontal = 14.dp),
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    val categoriesEnabled = state.canEdit && values.enabled
                    SettingsSection(stringResource(R.string.notification_settings_flights)) {
                        val rows = listOf(
                            NotificationPreferenceKey.FlightAssignments to R.string.notification_settings_assignments,
                            NotificationPreferenceKey.FlightInvitations to R.string.notification_settings_invitations,
                            NotificationPreferenceKey.ScheduleChanges to R.string.notification_settings_schedule,
                            NotificationPreferenceKey.ColleagueEdits to R.string.notification_settings_edits,
                            NotificationPreferenceKey.FlightReminders to R.string.notification_settings_reminders,
                        )
                        rows.forEachIndexed { index, (key, label) ->
                            if (index > 0) SettingsDivider()
                            NotificationSwitchRow(stringResource(label), values.value(key), categoriesEnabled,
                                onChange = { viewModel.setPreference(key, it) })
                        }
                    }
                    SettingsSection(stringResource(R.string.notification_settings_sharing)) {
                        NotificationSwitchRow(stringResource(R.string.notification_settings_direct_shares), values.directShares,
                            categoriesEnabled, onChange = { viewModel.setPreference(NotificationPreferenceKey.DirectShares, it) })
                        SettingsDivider()
                        NotificationSwitchRow(stringResource(R.string.notification_settings_station_shares), values.stationShares,
                            categoriesEnabled, onChange = { viewModel.setPreference(NotificationPreferenceKey.StationShares, it) })
                    }
                    SettingsSection(stringResource(R.string.notification_settings_saved_work)) {
                        NotificationSwitchRow(stringResource(R.string.notification_settings_sync_issues), values.syncIssues,
                            categoriesEnabled, onChange = { viewModel.setPreference(NotificationPreferenceKey.SyncIssues, it) })
                    }
                }
                SettingsSection(stringResource(R.string.notification_settings_device)) {
                    Row(Modifier.fillMaxWidth().heightIn(min = 48.dp)
                        .clickable(role = Role.Button) { openNotificationPreferences(context) }
                        .padding(horizontal = 14.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(stringResource(R.string.notification_settings_android), style = settingsLabelStyle(),
                                fontWeight = FontWeight.Bold)
                            Text(stringResource(if (systemAllowed) R.string.notification_settings_device_allowed
                                else R.string.notification_settings_device_blocked), style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Icon(Icons.Default.ChevronRight, null, Modifier.size(18.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }
    }
}

@Composable
private fun settingsLabelStyle() = MaterialTheme.typography.bodyMedium.copy(fontSize = 12.sp, lineHeight = 16.sp)

@Composable
private fun SettingsSection(title: String, content: @Composable ColumnScope.() -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(title, Modifier.padding(horizontal = 14.dp).semantics { heading() }, style = settingsLabelStyle(),
            fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurfaceVariant)
        SettingsGroup(content)
    }
}

@Composable
private fun SettingsGroup(content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surface), content = content)
}

@Composable
private fun SettingsDivider() {
    HorizontalDivider(Modifier.padding(horizontal = 14.dp), thickness = 0.5.dp,
        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.6f))
}

@Composable
private fun NotificationSwitchRow(label: String, checked: Boolean, enabled: Boolean, onChange: (Boolean) -> Unit) {
    val colors = MaterialTheme.colorScheme
    val switchColors = if (colors.isDark) SwitchDefaults.colors(
        checkedTrackColor = Color(0xFF81C995),
        checkedThumbColor = Color(0xFF08240F),
        uncheckedTrackColor = colors.surfaceContainerHighest,
        uncheckedThumbColor = colors.onSurfaceVariant,
    ) else SwitchDefaults.colors(
        checkedTrackColor = Color(0xFF248A3D), checkedThumbColor = Color.White,
        checkedBorderColor = Color.Transparent,
        uncheckedTrackColor = LoadingIndicatorGray, uncheckedThumbColor = Color.White,
        uncheckedBorderColor = Color.Transparent,
        disabledCheckedTrackColor = LoadingIndicatorGray, disabledCheckedThumbColor = Color.White,
        disabledCheckedBorderColor = Color.Transparent,
        disabledUncheckedTrackColor = LoadingIndicatorGray, disabledUncheckedThumbColor = Color.White,
        disabledUncheckedBorderColor = Color.Transparent,
    )
    Row(Modifier.fillMaxWidth().heightIn(min = 48.dp)
        .toggleable(checked, enabled = enabled, role = Role.Switch, onValueChange = onChange)
        .padding(horizontal = 14.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp)) {
        Text(label, Modifier.weight(1f), style = settingsLabelStyle(), fontWeight = FontWeight.Bold,
            color = colors.onSurface.copy(alpha = if (enabled) 1f else 0.5f))
        Switch(checked = checked, onCheckedChange = null, enabled = enabled, colors = switchColors)
    }
}
