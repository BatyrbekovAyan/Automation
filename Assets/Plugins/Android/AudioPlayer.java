package com.unity.audio;

import android.app.Activity;
import android.content.Context;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.media.AudioManager;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.os.PowerManager;

import com.google.android.exoplayer2.ExoPlayer;
import com.google.android.exoplayer2.MediaItem;
import com.google.android.exoplayer2.Player;
import com.google.android.exoplayer2.audio.AudioAttributes;
import com.google.android.exoplayer2.C;
import com.unity3d.player.UnityPlayer;

public class AudioPlayer {

    private static ExoPlayer player;
    private static Handler handler = new Handler(Looper.getMainLooper());
    private static Runnable progressTask;
    private static String currentUrl;

    // --- PROXIMITY SENSOR VARIABLES ---
    private static PowerManager.WakeLock proximityWakeLock;
    private static SensorManager sensorManager;
    private static Sensor proximitySensor;
    private static SensorEventListener proximityListener;
    private static AudioManager audioManager;
    private static boolean isProximityEnabled = false;
    
    // --- THE NEW SMART SENSOR TRACKERS ---
    private static boolean isNear = false;
    private static boolean pendingProximityDisable = false;

    public static void play(final String url) {
        final Activity activity = UnityPlayer.currentActivity;

        activity.runOnUiThread(() -> {

            if (player != null) {
                player.release();
            }

            currentUrl = url;

            player = new ExoPlayer.Builder(activity).build();

            AudioAttributes audioAttributes = new AudioAttributes.Builder()
                    .setUsage(C.USAGE_MEDIA)
                    .setContentType(C.AUDIO_CONTENT_TYPE_SPEECH)
                    .build();
            player.setAudioAttributes(audioAttributes, true);

            player.addListener(new Player.Listener() {
                @Override
                public void onPlaybackStateChanged(int playbackState) {
                    if (playbackState == Player.STATE_ENDED) {
                        UnityPlayer.UnitySendMessage("AudioController", "OnNativeAudioFinished", currentUrl);
                    }
                }
            });

            MediaItem item = MediaItem.fromUri(Uri.parse(url));
            player.setMediaItem(item);

            player.prepare();
            player.play();

            startProgressUpdates();
        });
    }

    private static void startProgressUpdates() {
        progressTask = new Runnable() {
            @Override
            public void run() {
                if (player != null) {
                    long pos = player.getCurrentPosition();
                    long dur = player.getDuration();

                    String data = currentUrl + "|" + (pos/1000f) + "|" + (dur/1000f);
                    UnityPlayer.UnitySendMessage("AudioController", "OnNativeProgress", data);

                    handler.postDelayed(this, 100);
                }
            }
        };
        handler.post(progressTask);
    }

    public static void pause() {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity != null) {
            activity.runOnUiThread(() -> {
                if (player != null) player.pause();
            });
        }
    }

    public static void resume() {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity != null) {
            activity.runOnUiThread(() -> {
                if (player != null) player.play();
            });
        }
    }

    public static void stop() {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity != null) {
            activity.runOnUiThread(() -> {
                // THE FIX 1: Stop spamming the slider updates instantly!
                if (progressTask != null) {
                    handler.removeCallbacks(progressTask);
                }
                if (player != null) {
                    player.release();
                    player = null;
                }
            });
        }
    }

    public static void seekTo(final float seconds) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity != null) {
            activity.runOnUiThread(() -> {
                if (player != null) {
                    player.seekTo((long)(seconds * 1000));
                }
            });
        }
    }

    // ⭐ THE UPGRADED PROXIMITY LOGIC
    public static void toggleProximity(boolean enable) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;

        if (audioManager == null) {
            audioManager = (AudioManager) activity.getSystemService(Context.AUDIO_SERVICE);
        }

        if (enable) {
            pendingProximityDisable = false; // Cancel any pending disconnects
            
            if (isProximityEnabled) return;
            isProximityEnabled = true;

            if (proximityWakeLock == null) {
                PowerManager powerManager = (PowerManager) activity.getSystemService(Context.POWER_SERVICE);
                if (powerManager != null && powerManager.isWakeLockLevelSupported(PowerManager.PROXIMITY_SCREEN_OFF_WAKE_LOCK)) {
                    proximityWakeLock = powerManager.newWakeLock(PowerManager.PROXIMITY_SCREEN_OFF_WAKE_LOCK, "UnityAudio::ProximityLock");
                }
            }
            
            if (proximityWakeLock != null && !proximityWakeLock.isHeld()) {
                proximityWakeLock.acquire();
            }

            if (sensorManager == null) {
                sensorManager = (SensorManager) activity.getSystemService(Context.SENSOR_SERVICE);
                if (sensorManager != null) {
                    proximitySensor = sensorManager.getDefaultSensor(Sensor.TYPE_PROXIMITY);
                }
            }

            if (proximitySensor != null && proximityListener == null) {
                proximityListener = new SensorEventListener() {
                    @Override
                    public void onSensorChanged(SensorEvent event) {
                        float distance = event.values[0];
                        // Measure if the phone is physically against the head
                        isNear = distance < proximitySensor.getMaximumRange() && distance < 5.0f;
                        
                        if (isNear) {
                            audioManager.setMode(AudioManager.MODE_IN_COMMUNICATION);
                            audioManager.setSpeakerphoneOn(false); 
                        } else {
                            audioManager.setMode(AudioManager.MODE_NORMAL);
                            audioManager.setSpeakerphoneOn(true); 
                            
                            // THE FIX 2: The audio ended while the phone was at the ear, 
                            // but NOW the user pulled it away. Release the screen safely!
                            if (pendingProximityDisable) {
                                actuallyDisableProximity();
                            }
                        }
                    }
                    @Override
                    public void onAccuracyChanged(Sensor sensor, int accuracy) {}
                };
                sensorManager.registerListener(proximityListener, proximitySensor, SensorManager.SENSOR_DELAY_NORMAL);
            }
        } else {
            // Audio stopped!
            if (!isProximityEnabled && !pendingProximityDisable) return;

            if (isNear) {
                // The phone is still at the ear. Wait for them to pull it away!
                pendingProximityDisable = true; 
            } else {
                // Phone is in their hand, disable instantly.
                actuallyDisableProximity();
            }
        }
    }

    // Handles the actual hardware shut down
    private static void actuallyDisableProximity() {
        isProximityEnabled = false;
        pendingProximityDisable = false;
        
        if (proximityWakeLock != null && proximityWakeLock.isHeld()) {
            proximityWakeLock.release();
        }

        if (sensorManager != null && proximityListener != null) {
            sensorManager.unregisterListener(proximityListener);
            proximityListener = null; // Important reset so it can be recreated!
        }

        if (audioManager != null) {
            audioManager.setMode(AudioManager.MODE_NORMAL);
            audioManager.setSpeakerphoneOn(true); 
        }
    }
}