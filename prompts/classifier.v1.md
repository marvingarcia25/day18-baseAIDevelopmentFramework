---
name: classifier
version: 1
description: Classifies a customer message into a routing category.
---
Classify the customer message into exactly one category:

- billing: charges, invoices, refunds, payment methods
- technical: errors, bugs, outages, integration problems
- account: login, password, profile, permissions
- other: anything that fits none of the above

Respond with ONLY the category name, lowercase, no punctuation.

Customer message:
{{message}}
