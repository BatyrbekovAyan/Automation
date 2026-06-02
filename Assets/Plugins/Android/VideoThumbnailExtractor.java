package com.unity.video;

import android.graphics.Bitmap;
import android.media.MediaMetadataRetriever;

import java.io.FileOutputStream;
import java.util.HashMap;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

// Job-based async thumbnail extraction from a (remote) video URL, polled from a
// Unity coroutine. Mirrors VideoThumbnailExtractor.mm's job/poll lifecycle so the
// C# bridge drives both platforms with the same loop. MediaMetadataRetriever does
// blocking network I/O, so each job runs on its own background thread.
public class VideoThumbnailExtractor {

    private static final int RUNNING = 0;
    private static final int DONE = 1;
    private static final int FAILED = 2;
    private static final int MAX_EDGE = 640; // long-edge cap for a thumbnail

    private static final class Job {
        volatile int status = RUNNING;
        volatile String error = "";
    }

    private static final ConcurrentHashMap<Integer, Job> jobs = new ConcurrentHashMap<>();
    private static final AtomicInteger nextId = new AtomicInteger(1);

    public static int startThumbExtract(final String url, final String outPath, final double timeSec) {
        final int jobId = nextId.getAndIncrement();
        final Job job = new Job();
        jobs.put(jobId, job);

        new Thread(new Runnable() {
            @Override
            public void run() {
                MediaMetadataRetriever retriever = new MediaMetadataRetriever();
                try {
                    retriever.setDataSource(url, new HashMap<String, String>());

                    long timeUs = (long) (timeSec * 1_000_000L);
                    Bitmap frame = retriever.getFrameAtTime(timeUs, MediaMetadataRetriever.OPTION_CLOSEST_SYNC);
                    if (frame == null) {
                        fail(job, "no frame at requested time");
                        return;
                    }

                    Bitmap scaled = scaleDown(frame);
                    FileOutputStream out = new FileOutputStream(outPath);
                    try {
                        scaled.compress(Bitmap.CompressFormat.JPEG, 90, out);
                        out.flush();
                    } finally {
                        out.close();
                    }
                    if (scaled != frame) scaled.recycle();
                    frame.recycle();

                    job.status = DONE;
                } catch (Throwable t) {
                    fail(job, t.getMessage() != null ? t.getMessage() : t.toString());
                } finally {
                    try { retriever.release(); } catch (Exception ignored) {}
                }
            }
        }).start();

        return jobId;
    }

    private static Bitmap scaleDown(Bitmap src) {
        int w = src.getWidth();
        int h = src.getHeight();
        int longest = Math.max(w, h);
        if (longest <= MAX_EDGE) return src;
        float ratio = (float) MAX_EDGE / (float) longest;
        int nw = Math.max(1, Math.round(w * ratio));
        int nh = Math.max(1, Math.round(h * ratio));
        return Bitmap.createScaledBitmap(src, nw, nh, true);
    }

    private static void fail(Job job, String message) {
        job.error = (message != null) ? message : "thumbnail extraction failed";
        job.status = FAILED;
    }

    public static int pollThumbExtract(int jobId) {
        Job job = jobs.get(jobId);
        return (job == null) ? FAILED : job.status;
    }

    public static String thumbExtractError(int jobId) {
        Job job = jobs.get(jobId);
        return (job == null) ? "unknown job" : job.error;
    }

    public static void freeThumbExtractJob(int jobId) {
        jobs.remove(jobId);
    }
}
