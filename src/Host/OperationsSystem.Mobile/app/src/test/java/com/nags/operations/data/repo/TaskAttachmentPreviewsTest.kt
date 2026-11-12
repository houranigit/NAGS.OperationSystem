package com.nags.operations.data.repo

import com.nags.operations.data.FlightServiceAttachmentRecord
import com.nags.operations.data.FlightTaskRecord
import com.nags.operations.data.FlightTaskSaved
import java.nio.file.Files
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class TaskAttachmentPreviewsTest {
    private val directory = Files.createTempDirectory("task-previews").toFile()
    private var owner: String? = "api|account"
    private var online = true
    private var requests = 0
    private val content = byteArrayOf(1, 2, 3, 4)
    private var fetch: suspend (String, String, String, String) -> ByteArray? = { _, _, _, _ -> content }
    private fun previews() = TaskAttachmentPreviews(directory, { owner }, { online }, { flight, service, attachment, uploadedAt ->
        requests++; fetch(flight, service, attachment, uploadedAt)
    })

    @After fun cleanup() { directory.deleteRecursively() }

    @Test fun downloadedMediaSurvivesRestartOfflineAndVersionsAndAccountsStayIsolated() = runBlocking {
        assertArrayEquals(content, previews().load("flight", "service", "attachment", "v1")!!.readBytes())
        online = false
        assertArrayEquals(content, previews().load("flight", "service", "attachment", "v1")!!.readBytes())
        assertNull(previews().load("flight", "service", "attachment", "v2"))
        assertNull(previews().load("flight", "other-service", "attachment", "v1"))
        for (differentOwner in listOf("api|other-account", "other-api|account")) {
            owner = differentOwner
            assertNull(previews().load("flight", "service", "attachment", "v1"))
        }
        assertEquals(1, requests)
    }

    @Test fun acknowledgedUploadIsAvailableOfflineAfterTheQueueReleasesItsFiles() = runBlocking {
        val media = FlightServiceAttachmentRecord("attachment", 2, "document.pdf", "application/pdf", content.size.toLong(),
            uploadedByUserId = "actor", uploadedAtUtc = "2026-11-11T12:00:00Z")
        val service = FlightTaskRecord("service", 0, "2026-11-11T11:00:00Z",
            createdByUserId = "actor", createdAtUtc = "2026-11-11T12:00:00Z", attachments = listOf(media))
        previews().recordAcknowledged("flight", FlightTaskSaved(service, "new-version"), mapOf(media.id to content))
        online = false
        assertArrayEquals(content, previews().load("flight", service.id, media.id, media.uploadedAtUtc)!!.readBytes())
        assertEquals(0, requests)
    }

    @Test fun clearedOrChangedSessionCannotPublishAnOldInFlightDownload() = runBlocking {
        val started = CompletableDeferred<Unit>()
        val response = CompletableDeferred<ByteArray?>()
        fetch = { _, _, _, _ -> started.complete(Unit); response.await() }
        val previews = previews()
        val late = async { previews.load("flight", "service", "attachment", "v1") }
        started.await()
        previews.clear()
        owner = "api|other-account"
        response.complete(content)
        assertNull(late.await())
        assertFalse(directory.walkTopDown().any { it.isFile })
    }
}
