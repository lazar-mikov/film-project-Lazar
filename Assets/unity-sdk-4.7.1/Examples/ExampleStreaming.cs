﻿/**
* (C) Copyright IBM Corp. 2015, 2020.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*      http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*
*/
#pragma warning disable 0649

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using IBM.Watson.SpeechToText.V1;
using IBM.Cloud.SDK;
using IBM.Cloud.SDK.Authentication;
using IBM.Cloud.SDK.Authentication.Iam;
using IBM.Cloud.SDK.Utilities;
using IBM.Cloud.SDK.DataTypes;

namespace IBM.Watsson.Examples
{
    public class ExampleStreaming : MonoBehaviour
    {
        #region PLEASE SET THESE VARIABLES IN THE INSPECTOR
        [Space(10)]
        [Tooltip("The service URL (optional). This defaults to \"https://stream.watsonplatform.net/speech-to-text/api\"")]
        [SerializeField]
        private string _serviceUrl;
        [Tooltip("Text field to display the results of streaming.")]
        public Text ResultsField;
        [Header("IAM Authentication")]
        [Tooltip("The IAM apikey.")]
        [SerializeField]
        private string _iamApikey;

        [Header("Parameters")]
        // https://www.ibm.com/watson/developercloud/speech-to-text/api/v1/curl.html?curl#get-model
        [Tooltip("The Model to use. This defaults to en-US_BroadbandModel")]
        [SerializeField]
        private string _recognizeModel;
        #endregion

        public GameObject videophonecall;
        public GameObject notificationVideoFight;
        public GameObject VideoFight;
        public GameObject notificationPhoto;
        public GameObject notificationMessage;
        public GameObject vibraion;
        public AudioSource audioData;
        public AudioClip clip;
        public float volume = 100f;

        private int _recordingRoutine = 0;
        private string _microphoneID = null;
        private AudioClip _recording = null;
        private int _recordingBufferSize = 1;
        private int _recordingHZ = 22050;

        private SpeechToTextService _service;

        void Start()
        {
            LogSystem.InstallDefaultReactors();
            Runnable.Run(CreateService());
            
        }

        private IEnumerator CreateService()
        {
            if (string.IsNullOrEmpty(_iamApikey))
            {
                throw new IBMException("Plesae provide IAM ApiKey for the service.");
            }

            IamAuthenticator authenticator = new IamAuthenticator(apikey: _iamApikey);

            //  Wait for tokendata
            while (!authenticator.CanAuthenticate())
                yield return null;

            _service = new SpeechToTextService(authenticator);
            if (!string.IsNullOrEmpty(_serviceUrl))
            {
                _service.SetServiceUrl(_serviceUrl);
            }
            _service.StreamMultipart = true;

            Active = true;
            StartRecording();
        }

        public bool Active
        {
            get { return _service.IsListening; }
            set
            {
                if (value && !_service.IsListening)
                {
                    _service.RecognizeModel = (string.IsNullOrEmpty(_recognizeModel) ? "en-US_BroadbandModel" : _recognizeModel);
                    _service.DetectSilence = true;
                    _service.EnableWordConfidence = true;
                    _service.EnableTimestamps = true;
                    _service.SilenceThreshold = 0.01f;
                    _service.MaxAlternatives = 1;
                    _service.EnableInterimResults = true;
                    _service.OnError = OnError;
                    _service.InactivityTimeout = -1;
                    _service.ProfanityFilter = false;
                    _service.SmartFormatting = true;
                    _service.SpeakerLabels = false;
                    _service.WordAlternativesThreshold = null;
                    _service.EndOfPhraseSilenceTime = null;
                    _service.StartListening(OnRecognize, OnRecognizeSpeaker);
                }
                else if (!value && _service.IsListening)
                {
                    _service.StopListening();
                }
            }
        }

        private void StartRecording()
        {
            if (_recordingRoutine == 0)
            {
                UnityObjectUtil.StartDestroyQueue();
                _recordingRoutine = Runnable.Run(RecordingHandler());
            }
        }

        private void StopRecording()
        {
            if (_recordingRoutine != 0)
            {
                Microphone.End(_microphoneID);
                Runnable.Stop(_recordingRoutine);
                _recordingRoutine = 0;
            }
        }

        private void OnError(string error)
        {
            Active = false;

            Log.Debug("ExampleStreaming.OnError()", "Error! {0}", error);
        }

        private IEnumerator RecordingHandler()
        {
            Log.Debug("ExampleStreaming.RecordingHandler()", "devices: {0}", Microphone.devices);
            _recording = Microphone.Start(_microphoneID, true, _recordingBufferSize, _recordingHZ);
            yield return null;      // let _recordingRoutine get set..

            if (_recording == null)
            {
                StopRecording();
                yield break;
            }

            bool bFirstBlock = true;
            int midPoint = _recording.samples / 2;
            float[] samples = null;

            while (_recordingRoutine != 0 && _recording != null)
            {
                int writePos = Microphone.GetPosition(_microphoneID);
                if (writePos > _recording.samples || !Microphone.IsRecording(_microphoneID))
                {
                    Log.Error("ExampleStreaming.RecordingHandler()", "Microphone disconnected.");

                    StopRecording();
                    yield break;
                }

                if ((bFirstBlock && writePos >= midPoint)
                  || (!bFirstBlock && writePos < midPoint))
                {
                    // front block is recorded, make a RecordClip and pass it onto our callback.
                    samples = new float[midPoint];
                    _recording.GetData(samples, bFirstBlock ? 0 : midPoint);

                    AudioData record = new AudioData();
                    record.MaxLevel = Mathf.Max(Mathf.Abs(Mathf.Min(samples)), Mathf.Max(samples));
                    record.Clip = AudioClip.Create("Recording", midPoint, _recording.channels, _recordingHZ, false);
                    record.Clip.SetData(samples, 0);

                    _service.OnListen(record);

                    bFirstBlock = !bFirstBlock;
                }
                else
                {
                    // calculate the number of samples remaining until we ready for a block of audio, 
                    // and wait that amount of time it will take to record.
                    int remaining = bFirstBlock ? (midPoint - writePos) : (_recording.samples - writePos);
                    float timeRemaining = (float)remaining / (float)_recordingHZ;

                    yield return new WaitForSeconds(timeRemaining);
                }
            }
            yield break;
        }

        private void OnRecognize(SpeechRecognitionEvent result)
        {
            if (result != null && result.results.Length > 0)
            {
                foreach (var res in result.results)
                {
                    foreach (var alt in res.alternatives)
                    {
                        string text = string.Format("{0} ({1}, {2:0.00})\n", alt.transcript, res.final ? "Final" : "Interim", alt.confidence);
                        Log.Debug("ExampleStreaming.OnRecognize()", text);
                        ResultsField.text = text;

                        if (alt.transcript.Contains("hide"))
                        {
                         //   videophonecall.SetActive(false);
                        }
                        if (alt.transcript.Contains("hi"))
                        {
                      //      videophonecall.SetActive(false);
                        }

                        if (alt.transcript.Contains("show"))
                        {
                           Invoke("showcall", 1);
                               
                        }
                        if (alt.transcript.Contains("door"))
                        {
                          //  videophonecall.SetActive(true);
                            Handheld.Vibrate();

                        }
                        
                        if (alt.transcript.Contains("fuck"))
                        {
                           // Invoke("showfightnotification", 28);
                            
                        }
                        
                        if (alt.transcript.Contains("doing"))
                        {
                          //  Invoke("showmessagenotification", 14);                          
                        }

                        if (alt.transcript.Contains("perfect"))
                        {
                            Invoke("showphotonotification", 0);
                            Invoke("showmessagenotification", 39);
                            Invoke("showfightnotification", 76);
                            Invoke("showfightvideo", 79);
                            Invoke("showcall", 162);


                        }
                        if (alt.transcript.Contains("go"))
                        {
                         //   notificationPhoto.SetActive(false);
                           
                        }

                    }

                    if (res.keywords_result != null && res.keywords_result.keyword != null)
                    {
                        foreach (var keyword in res.keywords_result.keyword)
                        {
                            Log.Debug("ExampleStreaming.OnRecognize()", "keyword: {0}, confidence: {1}, start time: {2}, end time: {3}", keyword.normalized_text, keyword.confidence, keyword.start_time, keyword.end_time);
                        }
                    }

                    if (res.word_alternatives != null)
                    {
                        foreach (var wordAlternative in res.word_alternatives)
                        {
                            Log.Debug("ExampleStreaming.OnRecognize()", "Word alternatives found. Start time: {0} | EndTime: {1}", wordAlternative.start_time, wordAlternative.end_time);
                            foreach (var alternative in wordAlternative.alternatives)
                                Log.Debug("ExampleStreaming.OnRecognize()", "\t word: {0} | confidence: {1}", alternative.word, alternative.confidence);
                        }
                    }
                }
            }
        }


        // voids that get triggered from the invokke method
        void showcall()
        {
            videophonecall.SetActive(true);
        }
        void showfightnotification()
        {
            notificationVideoFight.SetActive(true);
            Handheld.Vibrate();
            if (notificationVideoFight.activeInHierarchy == true)
           {
                AudioSource.PlayClipAtPoint(clip, transform.position, volume);
           }
        }

        void showfightvideo()
        {
            VideoFight.SetActive(true);
            if (VideoFight.activeInHierarchy == true)
            {
                Invoke("Disablefight", 30);
                Invoke("Disablefightnotificaion", 1);
            }

        }

        public void Disablefightnotificaion()
        {
            notificationVideoFight.SetActive(false);
        }

        public void Disablefight()
        {
            VideoFight.SetActive(false);
        }


        void showmessagenotification()
        {
            notificationMessage.SetActive(true);
            Handheld.Vibrate();
            if (notificationMessage.activeInHierarchy == true)
            {
               AudioSource.PlayClipAtPoint(clip, transform.position, volume);
           }
        }
        void showphotonotification()
        {
            notificationPhoto.SetActive(true);
            Handheld.Vibrate();
            if (notificationPhoto.activeInHierarchy == true)
            {
                AudioSource.PlayClipAtPoint(clip, transform.position, volume); 
            }
        }
        private void OnRecognizeSpeaker(SpeakerRecognitionEvent result)
        {
            if (result != null)
            {
                foreach (SpeakerLabelsResult labelResult in result.speaker_labels)
                {
                    Log.Debug("ExampleStreaming.OnRecognizeSpeaker()", string.Format("speaker result: {0} | confidence: {3} | from: {1} | to: {2}", labelResult.speaker, labelResult.from, labelResult.to, labelResult.confidence));
                }
            }
        }
    }
}
