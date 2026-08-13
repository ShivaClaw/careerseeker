@{
    Severity = @('Error', 'Warning')

    # These rule families were reviewed during R6(c). They are deliberate
    # repository conventions or analyzer limitations, not unexamined debt.
    # The rationale and raw finding counts are recorded in
    # docs/PSScriptAnalyzer.md.
    ExcludeRules = @(
        'PSAvoidUsingWriteHost'
        'PSReviewUnusedParameter'
        'PSUseBOMForUnicodeEncodedFile'
        'PSUseShouldProcessForStateChangingFunctions'
        'PSUseSingularNouns'
    )
}
