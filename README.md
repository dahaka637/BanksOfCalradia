<img width="1600" height="750" alt="back" src="https://github.com/user-attachments/assets/782e2675-fba9-46bd-ae41-e4a3f9ef1c15" />

# Banks of Calradia
### Mod Overview

**Banks of Calradia** is a comprehensive economic simulation mod for *Mount & Blade II: Bannerlord*, introducing a fully functional banking system. Players can **deposit savings**, **take loans**, and **influence city prosperity** through financial activity — creating a dynamic connection between personal wealth and the world economy.

---

## ⚙️ Core Concept
The mod adds banks to each major settlement. These institutions provide services for:
- **Savings accounts** that yield daily interest based on city prosperity.
- **Loans** with dynamically calculated credit limits, interest, and late fees.
- **Prosperity influence**, where large deposits stimulate city growth.
- **Passive Trade XP**, where daily banking profits grant gradual Trade experience.

Interest rates, loan values, and penalties are determined through adaptive formulas that react to both **city economics** and **player renown**, creating a realistic, evolving financial environment.

---

## 🧩 Technical Structure

The mod is organized into clear modules for maintainability and modular growth:

| Layer | Path | Role |
|--------|------|------|
| **Core** | `/Source/Core/` | Utilities, localization, and data helpers. |
| **Systems** | `/Source/Systems/` | Main logic for loans, savings, prosperity, and XP gains. |
| **UI** | `/Source/UI/` | Menus for savings, loan creation, and loan payment. |
| **ModuleData** | `/ModuleData/Languages/` | Localization files (EN, BR, SP). |

---

## 💰 Dynamic Economy System

All financial operations are dynamically influenced by both **city economics** and **player reputation**.

- **City Prosperity, Loyalty, and Security** affect savings interest, withdrawal fees, and late payment penalties.  
- **Clan Renown** determines available credit limits and reduces interest for reputable players.  
- **Loan penalties** and **interest rates** evolve as cities grow or decline economically.  
- **Withdrawal fees** reflect local financial stability and risk.  
- **Economic downturns** increase banking profits in poor towns while limiting growth in rich ones.  

This creates a living, reactive economy that balances realism with engaging gameplay.

---

## 🧠 Key Algorithms
- **Dynamic Interest:** Adjusts savings yields with higher returns in struggling cities.  
- **Loan Forecasting:** Calculates credit and interest based on prosperity, renown, and loan duration.  
- **Prosperity Forecasting:** Deposits contribute to city growth through a calibrated prosperity gain formula.  
- **Passive XP Gain:** Daily interest income passively grants Trade skill experience.  

---

## 🧱 Main Components

### `BankCampaignBehavior`
Registers and manages all systems — including daily updates, JSON persistence, and Trade XP generation.

### `FinanceProcessor`
Integrates with Bannerlord’s economy model to inject interest income and prosperity forecasts.

### `BankProsperityModel`
Applies prosperity bonuses derived from player deposits per city.

### `BankMenu_Savings` & `BankMenu_Loan`
Provide player interfaces for savings, withdrawals, and loan management with real-time calculations.

---

## 🌐 Localization
Full multilingual support through native XML and a custom `LocalizationHelper`.

Languages available:
- **English (default)**
- **Português (BR)**
- **Español (SP)**

---

## 📂 Directory Structure
```
BanksOfCalradia/
├── Source/
│   ├── Core/
│   ├── Systems/
│   ├── UI/
│   ├── SubModule.cs
├── ModuleData/
│   └── Languages/
│       ├── BR/
│       ├── EN/
│       ├── SP/
└── README.md
```

---

## Author & Credits
**Author:** Dahaka  
**Version:** 1.0.0 (Official Release)  
**License:** MIT — free for modification and distribution.

---

### Summary
**Banks of Calradia** introduces a deep, realistic financial framework to Bannerlord’s medieval economy — blending immersive simulation, passive skill growth, and a responsive world economy.  
Its modular design ensures clarity, stability, and easy extensibility for future expansions.
