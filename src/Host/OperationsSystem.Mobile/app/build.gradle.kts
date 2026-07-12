import java.net.URI
import java.util.Properties
import java.util.Locale
import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.ksp)
}

fun configuredValue(
    gradlePropertyName: String,
    environmentVariableName: String,
    trim: Boolean = true,
): String? {
    val rawValue = providers.gradleProperty(gradlePropertyName)
        .orElse(providers.environmentVariable(environmentVariableName))
        .orNull
        ?: return null
    val value = if (trim) rawValue.trim() else rawValue
    return value.takeIf(String::isNotEmpty)
}

fun normalizeBaseUrl(value: String): String = value.trim().trimEnd('/') + "/"

fun Properties.nonBlankProperty(name: String): String? =
    getProperty(name)?.trim()?.takeIf(String::isNotEmpty)

fun buildConfigString(value: String): String =
    "\"${value.replace("\\", "\\\\").replace("\"", "\\\"")}\""

fun releaseApiBaseUrlError(value: String?): String? {
    if (value.isNullOrBlank()) {
        return "Release API URL is missing. Set -Poperations.api.release.base.url or " +
            "OPERATIONS_API_RELEASE_BASE_URL."
    }

    val uri = try {
        URI(value)
    } catch (_: Exception) {
        return "Release API URL is not a valid absolute URI."
    }

    val scheme = uri.scheme?.lowercase(Locale.ROOT)
    if (scheme != "https" && scheme != "http")
        return "Release API URL must use http or https."

    val host = uri.host?.trim('[', ']')?.lowercase(Locale.ROOT)?.removeSuffix(".")
        ?: return "Release API URL must include a valid host."
    val isLocalHost = host == "localhost" ||
        host.endsWith(".localhost") ||
        host == "0.0.0.0" ||
        host == "::" ||
        host == "::1" ||
        host == "0:0:0:0:0:0:0:1" ||
        host == "127" ||
        host.startsWith("127.") ||
        host.startsWith("::ffff:127.") ||
        host == "10.0.2.2" ||
        host == "10.0.3.2"
    if (isLocalHost)
        return "Release API URL must not target localhost, loopback, or the Android emulator host."

    if (uri.userInfo != null)
        return "Release API URL must not contain embedded credentials."
    if (uri.port > 65_535)
        return "Release API URL contains an invalid port."
    if (uri.rawQuery != null || uri.rawFragment != null)
        return "Release API URL must not contain a query string or fragment."

    return null
}

// Debug reads local.properties. Release never does: its endpoint must come from
// gradle.properties, -Poperations.api.release.base.url, or OPERATIONS_API_RELEASE_BASE_URL.

val localProperties = Properties().apply {
    val f = rootProject.file("local.properties")
    if (f.exists()) f.inputStream().use { load(it) }
}
val debugApiBaseUrl = normalizeBaseUrl(
    configuredValue("operations.api.debug.base.url", "OPERATIONS_API_DEBUG_BASE_URL")
        ?: localProperties.nonBlankProperty("operations.api.debug.base.url")
        // Backward-compatible local developer setting; never used by Release.
        ?: localProperties.nonBlankProperty("operations.api.base.url")
        ?: "http://10.0.2.2:5211",
)
val releaseApiBaseUrl = configuredValue(
    "operations.api.release.base.url",
    "OPERATIONS_API_RELEASE_BASE_URL",
)
val releaseApiBaseUrlForCompilation = normalizeBaseUrl(
    releaseApiBaseUrl ?: "https://invalid.invalid",
)

val releaseSigningStoreFile = configuredValue(
    "operations.android.signing.store.file",
    "OPERATIONS_ANDROID_SIGNING_STORE_FILE",
)
val releaseSigningStorePassword = configuredValue(
    "operations.android.signing.store.password",
    "OPERATIONS_ANDROID_SIGNING_STORE_PASSWORD",
    trim = false,
)
val releaseSigningKeyAlias = configuredValue(
    "operations.android.signing.key.alias",
    "OPERATIONS_ANDROID_SIGNING_KEY_ALIAS",
)
val releaseSigningKeyPassword = configuredValue(
    "operations.android.signing.key.password",
    "OPERATIONS_ANDROID_SIGNING_KEY_PASSWORD",
    trim = false,
)
val releaseSigningSettings = linkedMapOf(
    "operations.android.signing.store.file / OPERATIONS_ANDROID_SIGNING_STORE_FILE" to releaseSigningStoreFile,
    "operations.android.signing.store.password / OPERATIONS_ANDROID_SIGNING_STORE_PASSWORD" to releaseSigningStorePassword,
    "operations.android.signing.key.alias / OPERATIONS_ANDROID_SIGNING_KEY_ALIAS" to releaseSigningKeyAlias,
    "operations.android.signing.key.password / OPERATIONS_ANDROID_SIGNING_KEY_PASSWORD" to releaseSigningKeyPassword,
)
val hasCompleteReleaseSigningConfiguration =
    releaseSigningSettings.values.all { !it.isNullOrBlank() }

android {
    namespace = "com.nags.operations"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.nags.operations"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "1.0.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    signingConfigs {
        if (hasCompleteReleaseSigningConfiguration) {
            create("release") {
                storeFile = rootProject.file(releaseSigningStoreFile!!)
                storePassword = releaseSigningStorePassword
                keyAlias = releaseSigningKeyAlias
                keyPassword = releaseSigningKeyPassword
            }
        }
    }

    buildTypes {
        getByName("debug") {
            buildConfigField("String", "API_BASE_URL", buildConfigString(debugApiBaseUrl))
        }
        getByName("release") {
            buildConfigField(
                "String",
                "API_BASE_URL",
                buildConfigString(releaseApiBaseUrlForCompilation),
            )
            if (hasCompleteReleaseSigningConfiguration) {
                signingConfig = signingConfigs.getByName("release")
            }

            // Keep shrinking disabled until the native client has regression tests and verified
            // keep rules for serialization, Room, Ktor, and SignalR.
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
    }
}

dependencies {
    val composeBom = platform(libs.compose.bom)
    implementation(composeBom)

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.core.splashscreen)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.navigation.compose)
    implementation(libs.androidx.datastore.preferences)

    implementation(libs.compose.ui)
    implementation(libs.compose.ui.tooling.preview)
    debugImplementation(libs.compose.ui.tooling)
    implementation(libs.compose.foundation)
    implementation(libs.compose.animation)
    implementation(libs.compose.material3)
    implementation(libs.compose.material.icons.extended)

    implementation(libs.ktor.client.android)
    implementation(libs.ktor.client.content.negotiation)
    implementation(libs.ktor.client.logging)
    implementation(libs.ktor.client.auth)
    implementation(libs.ktor.serialization.kotlinx.json)

    implementation(libs.kotlinx.serialization.json)
    implementation(libs.kotlinx.coroutines.android)

    implementation(libs.androidx.room.runtime)
    implementation(libs.androidx.room.ktx)
    ksp(libs.androidx.room.compiler)
    implementation(libs.androidx.work.runtime.ktx)
    implementation(libs.androidx.exifinterface)

    // Real-time sync over SignalR. The signalr client brings RxJava + OkHttp
    // transitively; slf4j-android is the Android logging binding so the
    // connection lifecycle is observable in logcat instead of silently NOP'd.
    implementation(libs.signalr)
    implementation(libs.slf4j.android)

    testImplementation(libs.junit4)
}

// Room schema export — generated JSONs land alongside the module so migrations
// can diff against the last committed schema once the catalogs evolve.
ksp {
    arg("room.schemaLocation", "$projectDir/schemas")
    arg("room.incremental", "true")
}

val validateReleaseApiConfiguration = tasks.register("validateReleaseApiConfiguration") {
    group = "verification"
    description = "Validates the explicit API endpoint required by Release variants."

    doLast {
        releaseApiBaseUrlError(releaseApiBaseUrl)?.let { error ->
            throw GradleException(error)
        }
    }
}

val validateReleaseSigningConfiguration = tasks.register("validateReleaseSigningConfiguration") {
    group = "verification"
    description = "Validates external signing inputs required for a distributable Release bundle."

    doLast {
        val missing = releaseSigningSettings
            .filterValues { it.isNullOrBlank() }
            .keys
        if (missing.isNotEmpty()) {
            throw GradleException(
                "Release bundle signing is not configured. Missing: ${missing.joinToString()}. " +
                    "Use environment variables or nontracked Gradle properties; never commit secrets.",
            )
        }

        val keystore = rootProject.file(requireNotNull(releaseSigningStoreFile))
        if (!keystore.isFile) {
            throw GradleException("Release signing keystore does not exist or is not a file: $keystore")
        }
    }
}

// Any Release compilation/package validates the endpoint. APK assembly is intentionally allowed
// without signing for CI verification; AAB distribution tasks additionally require signing.
tasks.configureEach {
    when (name) {
        "preReleaseBuild" -> dependsOn(validateReleaseApiConfiguration)
        "signReleaseBundle", "packageReleaseBundle", "bundleRelease" ->
            dependsOn(validateReleaseSigningConfiguration)
    }
}
