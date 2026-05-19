const state = {
  current: null,
  selected: new Set(),
  isSubmitting: false
};

const $ = (id) => document.getElementById(id);

const elements = {
  title: $("title"),
  subtitle: $("subtitle"),
  message: $("message"),
  completionCard: $("completion-card"),
  completionTitle: $("completion-title"),
  completionDetail: $("completion-detail"),
  questionCard: $("question-card"),
  questionKind: $("question-kind"),
  questionId: $("question-id"),
  questionText: $("question-text"),
  options: $("options"),
  progressCard: $("progress-card"),
  progressLine: $("question-progress-line"),
  submitSelectionButton: $("submit-selection-button"),
  feedbackCard: $("feedback-card"),
  feedbackStatus: $("feedback-status"),
  feedbackQuestion: $("feedback-question"),
  feedbackQuestionText: $("feedback-question-text"),
  feedbackLine: $("feedback-line"),
  feedbackProgress: $("feedback-progress"),
  feedbackOptions: $("feedback-options"),
  nextButton: $("next-button"),
  exitButton: $("exit-button")
};

elements.nextButton.addEventListener("click", async () => {
  await postJson("/api/next", {});
});

elements.submitSelectionButton.addEventListener("click", async () => {
  await submitSelectedAnswer();
});

elements.exitButton.addEventListener("click", async () => {
  await postJson("/api/exit", {});
});

async function loadState() {
  const response = await fetch("/api/state", { headers: { "Accept": "application/json" } });
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }

  render(await response.json());
}

async function postJson(url, body) {
  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Accept": "application/json",
      "Content-Type": "application/json"
    },
    body: JSON.stringify(body)
  });
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }

  render(await response.json());
}

function render(data) {
  state.current = data;
  state.isSubmitting = false;
  document.title = data.texts.title;
  elements.title.textContent = data.texts.title;
  elements.subtitle.textContent = data.texts.subtitle;

  renderStats(data.statistics);

  showMessage(data.message);
  renderCompletion(data);
  renderQuestion(data);
  renderFeedback(data);
}

function renderStats(statistics) {
  $("stat-total").textContent = statistics.total;
  $("stat-completed").textContent = statistics.completed;
  $("stat-remaining").textContent = statistics.remaining;
  $("stat-attempts").textContent = statistics.attempts;
  $("stat-correct").textContent = statistics.correct;
  $("stat-wrong").textContent = statistics.wrong;
  $("stat-completion-rate").textContent = formatPercent(statistics.completionRate);
  $("stat-accuracy-rate").textContent = formatPercent(statistics.accuracyRate);
}

function renderCompletion(data) {
  const isComplete = data.completed && !data.question && !data.feedback;
  elements.completionCard.hidden = !isComplete && !data.exited;
  elements.completionTitle.textContent = data.exited ? data.texts.goodbye : data.texts.finished;
  elements.completionDetail.textContent = data.exited
    ? `${data.texts.saved}（${data.progressFileName} / ${data.excelReportFileName}）`
    : `已归档 ${data.statistics.completed} / ${data.statistics.total} · 正确率 ${formatPercent(data.statistics.accuracyRate)}`;
}

function renderQuestion(data) {
  const shouldShow = data.question && !data.feedback && !data.completed && !data.exited;
  elements.questionCard.hidden = !shouldShow;
  elements.progressCard.hidden = !shouldShow;
  elements.submitSelectionButton.hidden = !shouldShow || !data.question?.isMultiple;

  if (!shouldShow) {
    return;
  }

  state.selected = new Set();
  elements.questionKind.textContent = data.question.typeDisplayName;
  elements.questionId.textContent = `第 ${data.question.number} 题`;
  elements.questionText.textContent = data.question.text;
  elements.submitSelectionButton.disabled = true;
  renderOptions(elements.options, data.question, state.selected, null, true);
  renderProgress(data.progress, data.archiveRule);
}

function renderProgress(progress, archiveRule) {
  const status = progress.completed ? "已归档" : "继续巩固";
  elements.progressLine.textContent = `作答 ${progress.attempts} · 对 ${progress.correctCount} · 错 ${progress.wrongCount} · ${status}`;
  elements.progressLine.title = archiveRule;
}

function renderFeedback(data) {
  const shouldShow = Boolean(data.feedback);
  elements.feedbackCard.hidden = !shouldShow;

  if (!shouldShow) {
    return;
  }

  const feedback = data.feedback;
  const question = data.question;
  elements.feedbackCard.classList.toggle("correct", feedback.isCorrect);
  elements.feedbackCard.classList.toggle("wrong", !feedback.isCorrect);
  elements.feedbackStatus.textContent = feedback.isCorrect ? `✓ ${data.texts.correct}` : `✗ ${data.texts.wrong}`;
  elements.feedbackQuestion.textContent = `${question.typeDisplayName} 第 ${question.number} 题`;
  elements.feedbackQuestionText.textContent = question.text;
  elements.feedbackLine.textContent = `你的答案：${formatAnswer(question, feedback.answer)} · ${data.texts.correctAnswer}：${formatAnswer(question, question.correctAnswer)}`;
  elements.feedbackProgress.textContent = `本题状态：${feedback.completed ? "已归档" : "继续巩固"} · 对/错 ${feedback.correctCount}/${feedback.wrongCount}`;
  renderOptions(elements.feedbackOptions, question, parseAnswerSet(feedback.answer), parseAnswerSet(question.correctAnswer), false);
}

function renderOptions(container, question, selectedSet, correctSet, interactive) {
  container.replaceChildren();
  Object.entries(question.options).forEach(([key, text]) => {
    const option = document.createElement(interactive ? "button" : "div");
    option.className = "option";
    option.type = interactive ? "button" : undefined;

    const isSelected = selectedSet.has(key);
    const isCorrect = correctSet?.has(key) ?? false;
    if (isSelected) option.classList.add("selected");
    if (!interactive && isCorrect) option.classList.add("correct");
    if (!interactive && isSelected && !isCorrect) option.classList.add("wrong");
    if (!interactive && !isSelected && isCorrect && question.isMultiple) option.classList.add("missed");

    const keyNode = document.createElement("span");
    keyNode.className = "option-key";
    keyNode.textContent = key;

    const textNode = document.createElement("span");
    textNode.textContent = text;

    option.append(keyNode, textNode);

    if (!interactive) {
      const marks = [];
      if (isCorrect) marks.push("正确");
      if (isSelected && isCorrect) marks.push("你的选择");
      if (isSelected && !isCorrect) marks.push("你的选择");
      if (!isSelected && isCorrect && question.isMultiple) marks.push("漏选");
      if (marks.length > 0) {
        const mark = document.createElement("span");
        mark.className = "option-mark";
        mark.textContent = marks.join(" · ");
        option.append(mark);
      }
    } else {
      option.addEventListener("click", () => chooseAnswer(key, question));
    }

    container.append(option);
  });
}

async function chooseAnswer(key, question) {
  if (state.isSubmitting) {
    return;
  }

  if (!question.isMultiple) {
    state.selected = new Set([key]);
    renderOptions(elements.options, question, state.selected, null, true);
    await submitSelectedAnswer();
    return;
  }

  if (state.selected.has(key)) {
    state.selected.delete(key);
  } else {
    state.selected.add(key);
  }

  renderOptions(elements.options, question, state.selected, null, true);
  elements.submitSelectionButton.disabled = state.selected.size === 0;
}

async function submitSelectedAnswer() {
  if (state.isSubmitting) {
    return;
  }

  if (state.selected.size === 0) {
    showMessage("请先点选答案。");
    return;
  }

  state.isSubmitting = true;
  elements.submitSelectionButton.disabled = true;
  try {
    await postJson("/api/answer", { answer: [...state.selected].sort().join("") });
  } catch (error) {
    state.isSubmitting = false;
    elements.submitSelectionButton.disabled = state.selected.size === 0;
    showMessage(`提交失败：${error.message}`);
  }
}

function showMessage(message) {
  elements.message.hidden = !message;
  elements.message.textContent = message ?? "";
}

function parseAnswerSet(answer) {
  return new Set((answer ?? "").toUpperCase().replace(/[^A-Z]/g, "").split("").filter(Boolean));
}

function formatAnswer(question, answer) {
  const answers = [...parseAnswerSet(answer)];
  if (answers.length === 0) {
    return answer;
  }

  if (question.isJudge) {
    return answers.map((key) => question.options[key] ?? key).join("、");
  }

  return answers.map((key) => question.options[key] ? `${key}(${question.options[key]})` : key).join("、");
}

function formatPercent(value) {
  return `${Number(value ?? 0).toFixed(2)}%`;
}

loadState().catch((error) => {
  showMessage(`加载失败：${error.message}`);
});
