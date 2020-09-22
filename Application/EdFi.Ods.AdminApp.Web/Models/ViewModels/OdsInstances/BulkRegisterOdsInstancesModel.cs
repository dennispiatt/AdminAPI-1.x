// SPDX-License-Identifier: Apache-2.0
// Licensed to the Ed-Fi Alliance under one or more agreements.
// The Ed-Fi Alliance licenses this file to you under the Apache License, Version 2.0.
// See the LICENSE and NOTICES files in the project root for more information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;
using EdFi.Ods.AdminApp.Management.Database;
using EdFi.Ods.AdminApp.Management.Database.Ods;
using EdFi.Ods.AdminApp.Management.Instances;
using EdFi.Ods.AdminApp.Management.OdsInstanceServices;
using EdFi.Ods.AdminApp.Web.Helpers;
using EdFi.Ods.AdminApp.Web.Infrastructure;
using FluentValidation;
using log4net;
using FluentValidation.Validators;

namespace EdFi.Ods.AdminApp.Web.Models.ViewModels.OdsInstances
{
    public class BulkRegisterOdsInstancesModel : IBulkRegisterOdsInstancesModel
    {
        [Accept(".csv")]
        [Display(Name = "Instances Data File")]
        public HttpPostedFileBase OdsInstancesFile { get; set; }
    }

    public class BulkRegisterOdsInstancesModelValidator : AbstractValidator<BulkRegisterOdsInstancesModel>
    {
        private readonly ILog _logger = LogManager.GetLogger("BulkRegisterOdsInstancesLog");
       
        private bool UniquenessRuleFailed { get; set; }

        private bool ValidHeadersRuleFailed { get; set; }

        public BulkRegisterOdsInstancesModelValidator(AdminAppDbContext database
            , ICloudOdsAdminAppSettingsApiModeProvider apiModeProvider
            , IDatabaseValidationService databaseValidationService
            , IDatabaseConnectionProvider databaseConnectionProvider)
        {
            RuleFor(m => m.OdsInstancesFile)
                .NotEmpty();

            RuleFor(m => m.OdsInstancesFile.FileName).NotNull().Must(x => x.ToLower().EndsWith(".csv"))
                .WithMessage("Please select a file with .csv format.");

            RuleFor(m => m.OdsInstancesFile)
                .Must(HaveValidHeaders)
                .When(m => m.OdsInstancesFile != null);

            RuleFor(m => m.OdsInstancesFile)
                .Must(HaveUniqueRecords)
                .When(m => m.OdsInstancesFile != null && !ValidHeadersRuleFailed);

            When(
                m => m.OdsInstancesFile != null && !UniquenessRuleFailed && !ValidHeadersRuleFailed,  () =>
                {
                    RuleFor(x => x.OdsInstancesFile)
                        .SafeCustom(
                            (model, context) =>
                            {
                                var validator = new RegisterOdsInstanceModelValidator(
                                    database, apiModeProvider, databaseValidationService,
                                    databaseConnectionProvider, true);

                                foreach (var record in model.DataRecords())
                                {
                                    var results = validator.Validate(record);
                                    if (!results.IsValid)
                                    {
                                        foreach (var failure in results.Errors)
                                        {
                                            _logger.Error($"Property: {failure.PropertyName} failed validation. Error: {failure.ErrorMessage}");
                                        }
                                    }
                                    context.AddFailures(results);
                                }
                            });
                });
        }

        public void GetDuplicates(List<RegisterOdsInstanceModel> dataRecords, out List<int?> duplicateNumericSuffixes, out List<string> duplicateDescriptions)
        {
            duplicateNumericSuffixes = dataRecords.GroupBy(x => x.NumericSuffix)
                .Where(g => g.Count() > 1).Select(x => x.Key).ToList();
            duplicateDescriptions = dataRecords.GroupBy(x => x.Description)
                .Where(g => g.Count() > 1).Select(x => x.Key).ToList();
        }

        private bool HaveValidHeaders(BulkRegisterOdsInstancesModel model, HttpPostedFileBase file,
            PropertyValidatorContext context)
        {
            var missingHeaders = file.MissingHeaders();

            if (missingHeaders == null || !file.MissingHeaders().Any())
            {
                return true;
            }

            ValidHeadersRuleFailed = true;
            context.Rule.MessageBuilder =
                c => $"Missing Headers: {string.Join(",", file.MissingHeaders())}";

            return false;

        }

        private bool HaveUniqueRecords(BulkRegisterOdsInstancesModel model, HttpPostedFileBase file, PropertyValidatorContext context)
        {
            GetDuplicates(model.OdsInstancesFile.DataRecords().ToList(), out var duplicateNumericSuffixes, out var duplicateDescriptions);

            var errorMessage = "";

            if (duplicateNumericSuffixes.Any())
            {
                errorMessage += $"The following instance numeric suffixes have duplicates : {string.Join(", ", duplicateNumericSuffixes)} \n";
            }

            if (duplicateDescriptions.Any())
            {
                errorMessage += $"The following instance descriptions have duplicates : {string.Join(", ", duplicateDescriptions)}";
            }

            if (errorMessage == "")
                return true;

            UniquenessRuleFailed = true;

            context.Rule.MessageBuilder = c => errorMessage;

            return false;
        }
    }
}
