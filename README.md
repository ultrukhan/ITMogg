# ITMogg — Shared Household Expense & Chore Tracker

**Course Project:** Software Engineering, Part 1  
**Program:** Computer Science, 3rd Year  
**Institution:** Ivan Franko National University of Lviv (LNU)

## 💡 What does "ITMogg" mean?
The word "Mogg" (or "Mog") is popular youth internet slang that means to outshine, eclipse, or dominate someone. Therefore, **ITMogg** playfully implies that our team completely outclasses, outshines, and dominates the competition in the field of Information Technology.

## 📌 The Problem
Roommates and shared-household members constantly disagree about who owes what for shared purchases, and chores go untracked or unevenly split. Manual tracking (spreadsheets, group chats) is tedious to maintain and gets abandoned within weeks — leading to disputes and unfair distribution of both cost and effort.

## 🎯 Target Users
- Student roommates sharing an apartment
- Dorm residents splitting groceries & bills
- Small shared households (2–6 people)
- Anyone tired of spreadsheets & IOUs

## ✨ Core Features
1. **Expense Splitting:** Manual entry or AI-parsed receipt photos (Gemini API), split equally, by amount, or by %.
2. **Smart Settlement:** Debt-simplification algorithm computes the minimum number of payments to settle everyone up.
3. **Chore Tracking:** Recurring chores with auto-rotation, completion history, and fairness tracking.
4. **Dashboard & Reports:** Balances, upcoming chores, and spending charts at a glance; CSV/PDF export.

## 👥 Team & Responsibilities
- **Svyatoslav** (Frontend + AI Integration): UI shell/navigation; Gemini receipt-parsing integration & review screen.
- **Anna** (Frontend): Expense entry, balances/settlement screens, dashboard & reports UI.
- **Uliana** (Backend): Household/expense data model, ADO.NET data access, expense CRUD logic.
- **Nazar** (Backend): Debt-simplification engine, chore scheduling logic, validation rules.
- **Solomiia** (QA): Test strategy, unit/integration tests, GitHub Actions CI, bug tracking.

## ⚙️ Repository & Process
- **Repository:** [github.com/ultrukhan/ITMogg](https://github.com/ultrukhan/ITMogg)
- **Architecture:** UI / Business Logic / Data Access / Tests (WPF/WinForms dashboard)
- **Project Management:** GitHub Projects board (Backlog → Done)
- **CI/CD:** GitHub Actions — build & test on every PR
- **Workflow:** Feature branches + PR review before merge to main
