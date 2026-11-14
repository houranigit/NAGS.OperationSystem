package com.nags.operations.ui.account

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.ChevronRight
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Person
import androidx.compose.material.icons.outlined.Sync
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.BuildConfig
import com.nags.operations.R
import com.nags.operations.ui.components.AppProfileAvatar
import com.nags.operations.ui.theme.AppThemeMode
import com.nags.operations.ui.theme.LocalAppAppearance

@Composable
internal fun ProfileMenuContent(displayName: String?, onClose: () -> Unit, onProfile: () -> Unit,
    onSync: () -> Unit, onPassword: () -> Unit, onNotificationSettings: () -> Unit, onLogout: () -> Unit) {
    Column(Modifier.fillMaxHeight().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            AppProfileAvatar(displayName, size = 52.dp)
            Spacer(Modifier.weight(1f))
            IconButton(onClick = onClose) { Icon(Icons.Default.Close, stringResource(R.string.profile_menu_close)) }
        }
        Text(displayName?.takeIf { it.isNotBlank() } ?: stringResource(R.string.account_title),
            style = MaterialTheme.typography.titleLarge.copy(fontSize = 18.sp), fontWeight = FontWeight.SemiBold,
            maxLines = 2, overflow = TextOverflow.Ellipsis)
        Column {
            ProfileMenuRow(Icons.Outlined.Person, stringResource(R.string.account_title), onClick = onProfile)
            ProfileMenuRow(Icons.Outlined.Sync, stringResource(R.string.profile_sync_hub),
                subtitle = stringResource(R.string.profile_sync_description), onClick = onSync)
            ProfileMenuRow(Icons.Outlined.Lock, stringResource(R.string.profile_password), onClick = onPassword)
            ProfileMenuRow(Icons.Outlined.Notifications, stringResource(R.string.profile_notifications), onClick = onNotificationSettings)
        }
        HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.65f))
        ProfileAppearanceSelector()
        HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.65f))
        ProfileMenuRow(Icons.AutoMirrored.Filled.Logout, stringResource(R.string.account_sign_out),
            destructive = true, onClick = onLogout)
        Text(stringResource(R.string.sync_center_version, BuildConfig.VERSION_NAME, BuildConfig.VERSION_CODE),
            style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp))
    }
}

@Composable
internal fun ProfileMenuRow(icon: ImageVector, title: String, modifier: Modifier = Modifier,
    subtitle: String? = null, destructive: Boolean = false, onClick: () -> Unit) {
    val tint = if (destructive) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
    Row(modifier.fillMaxWidth().heightIn(min = 52.dp).clickable(role = Role.Button, onClick = onClick)
        .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Icon(icon, null, Modifier.size(20.dp), tint = tint)
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(title, style = MaterialTheme.typography.bodyMedium.copy(fontSize = 13.sp), fontWeight = FontWeight.SemiBold,
                color = if (destructive) tint else MaterialTheme.colorScheme.onSurface)
            if (subtitle != null) Text(subtitle, style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (!destructive) Icon(Icons.Default.ChevronRight, null, Modifier.size(18.dp), tint = tint)
    }
}

@Composable
internal fun ProfileAppearanceSelector(modifier: Modifier = Modifier) {
    val appearance = LocalAppAppearance.current
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(stringResource(R.string.profile_appearance), Modifier.padding(horizontal = 12.dp),
            style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.SemiBold,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Row(Modifier.fillMaxWidth().height(IntrinsicSize.Min).background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(12.dp))
            .padding(3.dp).selectableGroup()) {
            AppThemeMode.entries.forEach { mode ->
                val selected = appearance.mode == mode
                Box(Modifier.weight(1f).fillMaxHeight().heightIn(min = 48.dp)
                    .background(if (selected) MaterialTheme.colorScheme.surface else androidx.compose.ui.graphics.Color.Transparent,
                        RoundedCornerShape(9.dp))
                    .selectable(selected, role = Role.RadioButton, onClick = { appearance.onChange(mode) })
                    .padding(horizontal = 4.dp, vertical = 8.dp), contentAlignment = Alignment.Center) {
                    Text(stringResource(when (mode) {
                        AppThemeMode.System -> R.string.profile_theme_system
                        AppThemeMode.Light -> R.string.profile_theme_light
                        AppThemeMode.Dark -> R.string.profile_theme_dark
                    }), style = MaterialTheme.typography.bodySmall, fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal,
                        color = if (selected) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = androidx.compose.ui.text.style.TextAlign.Center)
                }
            }
        }
    }
}

@Composable
internal fun ProfilePageHeader(title: String, onBack: () -> Unit, trailing: @Composable RowScope.() -> Unit = {}) {
    Row(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surface).statusBarsPadding()
        .padding(horizontal = 8.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.account_back)) }
        Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium.copy(fontSize = 16.sp),
            fontWeight = FontWeight.SemiBold)
        trailing()
    }
}

internal fun openNotificationPreferences(context: Context) {
    val intent = Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName)
    runCatching { context.startActivity(intent) }.getOrElse {
        context.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:${context.packageName}")))
    }
}
