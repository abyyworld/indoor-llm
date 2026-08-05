/**
 * Builds both study forms in one run. Paste into script.google.com, press Run, approve
 * the permission prompt, and read the two URLs out of the log.
 *
 * Why generated rather than clicked together by hand: the consent wording is the thing
 * the ethics committee approved, and a form rebuilt by hand after an amendment drifts
 * from the approved text. This file is the wording, in version control, next to the
 * study it consents to. Amend here, re-run, and the form matches the protocol again.
 *
 * WHERE THE DATA LIVES. Google is fine for most UK ethics approvals, but check yours:
 * if it requires institutional hosting or EU-only storage, use your university's
 * Qualtrics or Microsoft Forms instead and copy the wording below across verbatim.
 * The Unity side only needs a URL, so nothing in the app changes.
 *
 * WHAT GOES WHERE, and why:
 *   Consent + demographics  -- BEFORE, on a laptop. Consent has to be informed and
 *     freely given, which means readable, re-readable, and answerable without a headset
 *     on someone's face. Demographics ride along because they are the same posture and
 *     it saves a second sitting.
 *   Affect ratings          -- IN VR, already built. They measure how the room feels
 *     while the participant is in it; asking afterwards measures memory instead.
 *   Everything else         -- AFTER, on the laptop. Simulator sickness has to be after
 *     by definition, presence and the open questions need prose, and a debrief that
 *     happens before the study would tell them what it is about.
 */

var PARTICIPANT_QUESTION = 'Participant ID (the researcher will fill this in)';

function buildAll() {
  var consent = buildConsentForm();
  var post = buildPostSessionForm();

  Logger.log('\n=== Paste these into the Unity Study Control Panel ===\n');
  Logger.log('Consent form URL:       ' + consent);
  Logger.log('Questionnaire URL:      ' + post);
  Logger.log('\nResponses appear in the linked Sheet for each form.');
}

// --------------------------------------------------------------------- consent

function buildConsentForm() {
  var form = FormApp.create('Emotion Rooms — Information and Consent');
  form.setDescription(
    'Please read this before deciding whether to take part. Ask the researcher ' +
    'anything you want at any point, including after you have started.');
  form.setCollectEmail(false);
  form.setProgressBar(true);

  form.addTextItem()
    .setTitle(PARTICIPANT_QUESTION)
    .setRequired(true);

  form.addSectionHeaderItem()
    .setTitle('What the study is')
    .setHelpText(
      'You will wear a virtual reality headset and stand in a series of virtual rooms. ' +
      'Each room takes about 20 seconds. After each one you will say how the room made ' +
      'you feel, using a grid you point at inside the headset.\n\n' +
      'In the second half you will be shown rooms again and asked whether anything about ' +
      'them looks wrong, and what you would change. There are no right or wrong answers ' +
      'and nothing here is a test of you. The whole session takes about 45 minutes, ' +
      'including breaks.');

  form.addSectionHeaderItem()
    .setTitle('What we record')
    .setHelpText(
      'Your answers, and how you moved your head and the controller inside the headset. ' +
      'We do not record video, audio, or images of you, and we do not record anything ' +
      'about your face or eyes.\n\n' +
      'Your data is stored under a participant code, not your name. The list linking the ' +
      'code to you is kept separately and destroyed once data collection ends. Results ' +
      'are reported for the group, never for one person.');

  form.addSectionHeaderItem()
    .setTitle('Taking part is voluntary')
    .setHelpText(
      'You can stop at any moment, without giving a reason, and nothing follows from it. ' +
      'Say so, or take the headset off, and the researcher will end the session ' +
      'immediately.\n\n' +
      'You can also ask us to delete your data. Because data is stored under a code, ask ' +
      'within two weeks of your session, while the code can still be matched to you.');

  form.addSectionHeaderItem()
    .setTitle('Risks')
    .setHelpText(
      'Some people feel briefly dizzy, disoriented or nauseous in virtual reality. If ' +
      'that happens, tell the researcher and stop. Do not take part if you have a ' +
      'condition where this would be unsafe, or if you are unwell today.\n\n' +
      'You will be standing. There is a clear floor area, but tell the researcher if ' +
      'standing for 45 minutes is a problem, as we can seat you.');

  var statements = [
    'I have read and understood the information above.',
    'I have had the chance to ask questions, and they were answered.',
    'I understand that taking part is voluntary and that I can stop at any time without ' +
      'giving a reason and without any consequences.',
    'I understand what will be recorded, and that it is stored under a code rather than ' +
      'my name.',
    'I understand that anonymised data may be published and shared with other ' +
      'researchers, and that I cannot be identified from it.',
    'I am 18 or over.',
    'I agree to take part.'
  ];

  // Each statement is its own required item. A single "I agree to everything" tick is
  // one click for a participant who read none of it; separate items at least make the
  // separate agreements explicit, which is what informed consent means.
  for (var i = 0; i < statements.length; i++) {
    form.addMultipleChoiceItem()
      .setTitle(statements[i])
      .setChoiceValues(['Yes'])
      .setRequired(true);
  }

  form.addSectionHeaderItem().setTitle('A few details about you');

  form.addTextItem().setTitle('Age').setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('Gender')
    .setChoiceValues(['Woman', 'Man', 'Non-binary', 'Prefer to self-describe',
                      'Prefer not to say'])
    .showOtherOption(true)
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('Do you wear glasses or contact lenses?')
    .setChoiceValues(['No', 'Yes, and I am wearing them now',
                      'Yes, but I am not wearing them now'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('Do you have any colour vision deficiency?')
    .setHelpText('The study varies the colour of rooms, so this matters for the results. ' +
                 'It does not stop you taking part.')
    .setChoiceValues(['No', 'Yes', 'Not sure'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('How often have you used virtual reality before?')
    .setChoiceValues(['Never', 'Once or twice', 'A few times a year',
                      'Monthly', 'Weekly or more'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('How well rested do you feel right now?')
    .setChoiceValues(['1 — not at all', '2', '3', '4', '5 — very well rested'])
    .setRequired(true);

  linkSheet(form, 'Emotion Rooms — consent responses');
  return form.getPublishedUrl();
}

// ----------------------------------------------------------------- post-session

function buildPostSessionForm() {
  var form = FormApp.create('Emotion Rooms — After the session');
  form.setDescription('Thank you. A few last questions, then we are done.');
  form.setCollectEmail(false);
  form.setProgressBar(true);

  form.addTextItem()
    .setTitle(PARTICIPANT_QUESTION)
    .setRequired(true);

  // Simulator sickness. Kennedy et al. (1993) short form: this is a safety and
  // data-quality measure, not an outcome. Someone who felt sick rated rooms while
  // feeling sick, and that has to be visible in the analysis rather than guessed at.
  form.addSectionHeaderItem()
    .setTitle('How you feel now')
    .setHelpText('For each one, choose how much you feel it at this moment.');

  var symptoms = ['General discomfort', 'Fatigue', 'Headache', 'Eye strain',
                  'Difficulty focusing', 'Nausea', 'Dizziness', 'Blurred vision'];
  for (var i = 0; i < symptoms.length; i++) {
    form.addMultipleChoiceItem()
      .setTitle(symptoms[i])
      .setChoiceValues(['None', 'Slight', 'Moderate', 'Severe'])
      .setRequired(true);
  }

  form.addSectionHeaderItem()
    .setTitle('Being there')
    .setHelpText('1 means not at all, 7 means completely.');

  var presence = [
    'I felt like I was really standing in those rooms.',
    'The rooms felt like places rather than pictures.',
    'I was aware of the real room around me.',
    'The furniture and layout felt like a real living space.'
  ];
  for (var j = 0; j < presence.length; j++) {
    form.addScaleItem()
      .setTitle(presence[j])
      .setBounds(1, 7)
      .setLabels('Not at all', 'Completely')
      .setRequired(true);
  }

  // The open questions. These are the ones the VR instrument cannot ask: what someone
  // was actually doing when they said a room looked wrong. Free text because the point
  // is to find strategies nobody thought to put on a multiple-choice list.
  form.addSectionHeaderItem()
    .setTitle('The second half');

  form.addParagraphTextItem()
    .setTitle('When you were asked whether something looked wrong in a room, how did you ' +
              'decide?')
    .setHelpText('Whatever you actually did, including guessing.')
    .setRequired(true);

  form.addParagraphTextItem()
    .setTitle('Was there anything you noticed felt wrong but could not put your finger on?')
    .setRequired(false);

  form.addParagraphTextItem()
    .setTitle('When you changed something about a room, what were you trying to achieve?')
    .setRequired(false);

  form.addMultipleChoiceItem()
    .setTitle('How confident are you that you spotted the rooms that had something wrong?')
    .setChoiceValues(['1 — not at all', '2', '3', '4', '5 — very confident'])
    .setRequired(true);

  form.addSectionHeaderItem()
    .setTitle('Anything else');

  form.addParagraphTextItem()
    .setTitle('Anything that felt odd, uncomfortable, unclear, or worth telling us?')
    .setRequired(false);

  form.addSectionHeaderItem()
    .setTitle('What this was about')
    .setHelpText(
      'The rooms were designed by an AI system asked to make a space feel a particular ' +
      'way, by choosing colour, brightness and materials.\n\n' +
      'In the second half, some rooms had one of those choices deliberately replaced ' +
      'with a value the system had picked for a different feeling. We did not tell you ' +
      'which, because knowing would have changed what you noticed. We were measuring ' +
      'whether people can tell when a system like this has got something wrong, and ' +
      'whether they can say which part.\n\n' +
      'Nothing you did was right or wrong, and rooms where you noticed nothing are just ' +
      'as informative as rooms where you did. If you have questions about this, or want ' +
      'your data removed, contact the researcher.');

  form.addMultipleChoiceItem()
    .setTitle('I have read the explanation above.')
    .setChoiceValues(['Yes'])
    .setRequired(true);

  linkSheet(form, 'Emotion Rooms — post-session responses');
  return form.getPublishedUrl();
}

// --------------------------------------------------------------------- helpers

function linkSheet(form, name) {
  var sheet = SpreadsheetApp.create(name);
  form.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());
}
