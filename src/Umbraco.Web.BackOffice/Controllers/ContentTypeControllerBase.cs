﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Net.Http.Headers;
using Umbraco.Core;
using Umbraco.Core.Dictionary;
using Umbraco.Core.Exceptions;
using Umbraco.Core.Mapping;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Extensions;
using Umbraco.Web.Common.Attributes;
using Umbraco.Web.Common.Exceptions;
using Umbraco.Web.Editors;
using Umbraco.Web.Models.ContentEditing;

namespace Umbraco.Web.BackOffice.Controllers
{
    /// <summary>
    /// Am abstract API controller providing functionality used for dealing with content and media types
    /// </summary>
    [PluginController(Constants.Web.Mvc.BackOfficeApiArea)]
    //[PrefixlessBodyModelValidator] //TODO reintroduce
    public abstract class ContentTypeControllerBase<TContentType> : UmbracoAuthorizedJsonController
        where TContentType : class, IContentTypeComposition
    {
        private readonly EditorValidatorCollection _editorValidatorCollection;

        protected ContentTypeControllerBase(
            ICultureDictionary cultureDictionary,
            EditorValidatorCollection editorValidatorCollection,
            IContentTypeService contentTypeService,
            IMediaTypeService mediaTypeService,
            IMemberTypeService memberTypeService,
            UmbracoMapper umbracoMapper,
            ILocalizedTextService localizedTextService)
        {
            _editorValidatorCollection = editorValidatorCollection ?? throw new ArgumentNullException(nameof(editorValidatorCollection));
            CultureDictionary = cultureDictionary ?? throw new ArgumentNullException(nameof(cultureDictionary));
            ContentTypeService = contentTypeService ?? throw new ArgumentNullException(nameof(contentTypeService));
            MediaTypeService = mediaTypeService ?? throw new ArgumentNullException(nameof(mediaTypeService));
            MemberTypeService = memberTypeService ?? throw new ArgumentNullException(nameof(memberTypeService));
            UmbracoMapper = umbracoMapper ?? throw new ArgumentNullException(nameof(umbracoMapper));
            LocalizedTextService = localizedTextService ?? throw new ArgumentNullException(nameof(localizedTextService));
        }

        protected ICultureDictionary CultureDictionary { get; }
        public IContentTypeService ContentTypeService { get; }
        public IMediaTypeService MediaTypeService { get; }
        public IMemberTypeService MemberTypeService { get; }
        public UmbracoMapper UmbracoMapper { get; }
        public ILocalizedTextService LocalizedTextService { get; }

        /// <summary>
        /// Returns the available composite content types for a given content type
        /// </summary>
        /// <param name="type"></param>
        /// <param name="filterContentTypes">
        /// This is normally an empty list but if additional content type aliases are passed in, any content types containing those aliases will be filtered out
        /// along with any content types that have matching property types that are included in the filtered content types
        /// </param>
        /// <param name="filterPropertyTypes">
        /// This is normally an empty list but if additional property type aliases are passed in, any content types that have these aliases will be filtered out.
        /// This is required because in the case of creating/modifying a content type because new property types being added to it are not yet persisted so cannot
        /// be looked up via the db, they need to be passed in.
        /// </param>
        /// <param name="contentTypeId"></param>
        /// <param name="isElement">Wether the composite content types should be applicable for an element type</param>
        /// <returns></returns>
        protected IEnumerable<Tuple<EntityBasic, bool>> PerformGetAvailableCompositeContentTypes(int contentTypeId,
            UmbracoObjectTypes type,
            string[] filterContentTypes,
            string[] filterPropertyTypes,
            bool isElement)
        {
            IContentTypeComposition source = null;

            //below is all ported from the old doc type editor and comes with the same weaknesses /insanity / magic

            IContentTypeComposition[] allContentTypes;

            switch (type)
            {
                case UmbracoObjectTypes.DocumentType:
                    if (contentTypeId > 0)
                    {
                        source = ContentTypeService.Get(contentTypeId);
                        if (source == null) throw new HttpResponseException(HttpStatusCode.NotFound);
                    }
                    allContentTypes = ContentTypeService.GetAll().Cast<IContentTypeComposition>().ToArray();
                    break;

                case UmbracoObjectTypes.MediaType:
                    if (contentTypeId > 0)
                    {
                        source =MediaTypeService.Get(contentTypeId);
                        if (source == null) throw new HttpResponseException(HttpStatusCode.NotFound);
                    }
                    allContentTypes =MediaTypeService.GetAll().Cast<IContentTypeComposition>().ToArray();
                    break;

                case UmbracoObjectTypes.MemberType:
                    if (contentTypeId > 0)
                    {
                        source = MemberTypeService.Get(contentTypeId);
                        if (source == null) throw new HttpResponseException(HttpStatusCode.NotFound);
                    }
                    allContentTypes = MemberTypeService.GetAll().Cast<IContentTypeComposition>().ToArray();
                    break;

                default:
                    throw new ArgumentOutOfRangeException("The entity type was not a content type");
            }

            var availableCompositions = ContentTypeService.GetAvailableCompositeContentTypes(source, allContentTypes, filterContentTypes, filterPropertyTypes, isElement);



            var currCompositions = source == null ? new IContentTypeComposition[] { } : source.ContentTypeComposition.ToArray();
            var compAliases = currCompositions.Select(x => x.Alias).ToArray();
            var ancestors = availableCompositions.Ancestors.Select(x => x.Alias);

            return availableCompositions.Results
                .Select(x => new Tuple<EntityBasic, bool>(UmbracoMapper.Map<IContentTypeComposition, EntityBasic>(x.Composition), x.Allowed))
                .Select(x =>
                {
                    //we need to ensure that the item is enabled if it is already selected
                    // but do not allow it if it is any of the ancestors
                    if (compAliases.Contains(x.Item1.Alias) && ancestors.Contains(x.Item1.Alias) == false)
                    {
                        //re-set x to be allowed (NOTE: I didn't know you could set an enumerable item in a lambda!)
                        x = new Tuple<EntityBasic, bool>(x.Item1, true);
                    }

                    //translate the name
                    x.Item1.Name = TranslateItem(x.Item1.Name);

                    var contentType = allContentTypes.FirstOrDefault(c => c.Key == x.Item1.Key);
                    var containers = GetEntityContainers(contentType, type)?.ToArray();
                    var containerPath = $"/{(containers != null && containers.Any() ? $"{string.Join("/", containers.Select(c => c.Name))}/" : null)}";
                    x.Item1.AdditionalData["containerPath"] = containerPath;

                    return x;
                })
                .ToList();
        }

        private IEnumerable<EntityContainer> GetEntityContainers(IContentTypeComposition contentType, UmbracoObjectTypes type)
        {
            if (contentType == null)
            {
                return null;
            }

            switch (type)
            {
                case UmbracoObjectTypes.DocumentType:
                    return ContentTypeService.GetContainers(contentType as IContentType);
                case UmbracoObjectTypes.MediaType:
                    return MediaTypeService.GetContainers(contentType as IMediaType);
                case UmbracoObjectTypes.MemberType:
                    return new EntityContainer[0];
                default:
                    throw new ArgumentOutOfRangeException("The entity type was not a content type");
            }
        }

        /// <summary>
        /// Returns a list of content types where a particular composition content type is used
        /// </summary>
        /// <param name="type">Type of content Type, eg documentType or mediaType</param>
        /// <param name="contentTypeId">Id of composition content type</param>
        /// <returns></returns>
        protected IEnumerable<EntityBasic> PerformGetWhereCompositionIsUsedInContentTypes(int contentTypeId, UmbracoObjectTypes type)
        {
            var id = 0;

            if (contentTypeId > 0)
            {
                IContentTypeComposition source;

                switch (type)
                {
                    case UmbracoObjectTypes.DocumentType:
                        source = ContentTypeService.Get(contentTypeId);
                        break;

                    case UmbracoObjectTypes.MediaType:
                        source =MediaTypeService.Get(contentTypeId);
                        break;

                    case UmbracoObjectTypes.MemberType:
                        source = MemberTypeService.Get(contentTypeId);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(type));
                }

                if (source == null)
                    throw new HttpResponseException(HttpStatusCode.NotFound);

                id = source.Id;
            }

            IEnumerable<IContentTypeComposition> composedOf;

            switch (type)
            {
                case UmbracoObjectTypes.DocumentType:
                    composedOf = ContentTypeService.GetComposedOf(id);
                    break;

                case UmbracoObjectTypes.MediaType:
                    composedOf =MediaTypeService.GetComposedOf(id);
                    break;

                case UmbracoObjectTypes.MemberType:
                    composedOf = MemberTypeService.GetComposedOf(id);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }

            EntityBasic TranslateName(EntityBasic e)
            {
                e.Name = TranslateItem(e.Name);
                return e;
            }

            return composedOf
                .Select(UmbracoMapper.Map<IContentTypeComposition, EntityBasic>)
                .Select(TranslateName)
                .ToList();
        }

        protected string TranslateItem(string text)
        {
            if (text == null)
                return null;

            if (text.StartsWith("#") == false)
                return text;

            text = text.Substring(1);
            return CultureDictionary[text].IfNullOrWhiteSpace(text);
        }

        protected TContentType PerformPostSave<TContentTypeDisplay, TContentTypeSave, TPropertyType>(
            TContentTypeSave contentTypeSave,
            Func<int, TContentType> getContentType,
            Action<TContentType> saveContentType,
            Action<TContentTypeSave> beforeCreateNew = null)
            where TContentTypeDisplay : ContentTypeCompositionDisplay
            where TContentTypeSave : ContentTypeSave<TPropertyType>
            where TPropertyType : PropertyTypeBasic
        {
            var ctId = Convert.ToInt32(contentTypeSave.Id);
            var ct = ctId > 0 ? getContentType(ctId) : null;
            if (ctId > 0 && ct == null) throw new HttpResponseException(HttpStatusCode.NotFound);

            //Validate that there's no other ct with the same alias
            // it in fact cannot be the same as any content type alias (member, content or media) because
            // this would interfere with how ModelsBuilder works and also how many of the published caches
            // works since that is based on aliases.
            var allAliases = ContentTypeService.GetAllContentTypeAliases();
            var exists = allAliases.InvariantContains(contentTypeSave.Alias);
            if (exists && (ctId == 0 || !ct.Alias.InvariantEquals(contentTypeSave.Alias)))
            {
                ModelState.AddModelError("Alias", LocalizedTextService.Localize("editcontenttype/aliasAlreadyExists"));
            }

            // execute the external validators
            ValidateExternalValidators(ModelState, contentTypeSave);

            if (ModelState.IsValid == false)
            {
                throw CreateModelStateValidationException<TContentTypeSave, TContentTypeDisplay>(ctId, contentTypeSave, ct);
            }

            //filter out empty properties
            contentTypeSave.Groups = contentTypeSave.Groups.Where(x => x.Name.IsNullOrWhiteSpace() == false).ToList();
            foreach (var group in contentTypeSave.Groups)
            {
                group.Properties = group.Properties.Where(x => x.Alias.IsNullOrWhiteSpace() == false).ToList();
            }

            if (ctId > 0)
            {
                //its an update to an existing content type

                //This mapping will cause a lot of content type validation to occur which we need to deal with
                try
                {
                    UmbracoMapper.Map(contentTypeSave, ct);
                }
                catch (Exception ex)
                {
                    var responseEx = CreateInvalidCompositionResponseException<TContentTypeDisplay, TContentTypeSave, TPropertyType>(ex, contentTypeSave, ct, ctId);
                    if (responseEx != null) throw responseEx;
                }

                var exResult = CreateCompositionValidationExceptionIfInvalid<TContentTypeSave, TPropertyType, TContentTypeDisplay>(contentTypeSave, ct);
                if (exResult != null) throw exResult;

                saveContentType(ct);

                return ct;
            }
            else
            {
                if (beforeCreateNew != null)
                {
                    beforeCreateNew(contentTypeSave);
                }

                //check if the type is trying to allow type 0 below itself - id zero refers to the currently unsaved type
                //always filter these 0 types out
                var allowItselfAsChild = false;
                var allowIfselfAsChildSortOrder = -1;
                if (contentTypeSave.AllowedContentTypes != null)
                {
                    allowIfselfAsChildSortOrder = contentTypeSave.AllowedContentTypes.IndexOf(0);
                    allowItselfAsChild = contentTypeSave.AllowedContentTypes.Any(x => x == 0);

                    contentTypeSave.AllowedContentTypes = contentTypeSave.AllowedContentTypes.Where(x => x > 0).ToList();
                }

                //save as new

                TContentType newCt = null;
                try
                {
                    //This mapping will cause a lot of content type validation to occur which we need to deal with
                    newCt = UmbracoMapper.Map<TContentType>(contentTypeSave);
                }
                catch (Exception ex)
                {
                    var responseEx = CreateInvalidCompositionResponseException<TContentTypeDisplay, TContentTypeSave, TPropertyType>(ex, contentTypeSave, ct, ctId);
                    throw responseEx ?? ex;
                }

                var exResult = CreateCompositionValidationExceptionIfInvalid<TContentTypeSave, TPropertyType, TContentTypeDisplay>(contentTypeSave, newCt);
                if (exResult != null) throw exResult;

                //set id to null to ensure its handled as a new type
                contentTypeSave.Id = null;
                contentTypeSave.CreateDate = DateTime.Now;
                contentTypeSave.UpdateDate = DateTime.Now;

                saveContentType(newCt);

                //we need to save it twice to allow itself under itself.
                if (allowItselfAsChild && newCt != null)
                {
                    newCt.AllowedContentTypes =
                        newCt.AllowedContentTypes.Union(
                            new []{ new ContentTypeSort(newCt.Id, allowIfselfAsChildSortOrder) }
                        );
                    saveContentType(newCt);
                }
                return newCt;
            }
        }

        private void ValidateExternalValidators(ModelStateDictionary modelState, object model)
        {
            var modelType = model.GetType();

                       var validationResults = _editorValidatorCollection
                           .Where(x => x.ModelType == modelType)
                           .SelectMany(x => x.Validate(model))
                           .Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage) && x.MemberNames.Any());

                       foreach (var r in validationResults)
                       foreach (var m in r.MemberNames)
                           modelState.AddModelError(m, r.ErrorMessage);
        }

        /// <summary>
        /// Move
        /// </summary>
        /// <param name="move"></param>
        /// <param name="getContentType"></param>
        /// <param name="doMove"></param>
        /// <returns></returns>
        protected IActionResult PerformMove(
            MoveOrCopy move,
            Func<int, TContentType> getContentType,
            Func<TContentType, int, Attempt<OperationResult<MoveOperationStatusType>>> doMove)
        {
            var toMove = getContentType(move.Id);
            if (toMove == null)
            {
                return NotFound();
            }

            var result = doMove(toMove, move.ParentId);
            if (result.Success)
            {
                return Content(toMove.Path, MediaTypeNames.Text.Plain, Encoding.UTF8);
            }

            switch (result.Result.Result)
            {
                case MoveOperationStatusType.FailedParentNotFound:
                    return NotFound();
                case MoveOperationStatusType.FailedCancelledByEvent:
                    //returning an object of INotificationModel will ensure that any pending
                    // notification messages are added to the response.
                    throw HttpResponseException.CreateValidationErrorResponse(new SimpleNotificationModel());
                case MoveOperationStatusType.FailedNotAllowedByPath:
                    var notificationModel = new SimpleNotificationModel();
                    notificationModel.AddErrorNotification(LocalizedTextService.Localize("moveOrCopy/notAllowedByPath"), "");
                    throw HttpResponseException.CreateValidationErrorResponse(notificationModel);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Move
        /// </summary>
        /// <param name="move"></param>
        /// <param name="getContentType"></param>
        /// <param name="doCopy"></param>
        /// <returns></returns>
        protected IActionResult PerformCopy(
            MoveOrCopy move,
            Func<int, TContentType> getContentType,
            Func<TContentType, int, Attempt<OperationResult<MoveOperationStatusType, TContentType>>> doCopy)
        {
            var toMove = getContentType(move.Id);
            if (toMove == null)
            {
                return NotFound();
            }

            var result = doCopy(toMove, move.ParentId);
            if (result.Success)
            {
                return Content(toMove.Path, MediaTypeNames.Text.Plain, Encoding.UTF8);
            }

            switch (result.Result.Result)
            {
                case MoveOperationStatusType.FailedParentNotFound:
                    return NotFound();
                case MoveOperationStatusType.FailedCancelledByEvent:
                    //returning an object of INotificationModel will ensure that any pending
                    // notification messages are added to the response.
                    throw HttpResponseException.CreateValidationErrorResponse(new SimpleNotificationModel());
                case MoveOperationStatusType.FailedNotAllowedByPath:
                    var notificationModel = new SimpleNotificationModel();
                    notificationModel.AddErrorNotification(LocalizedTextService.Localize("moveOrCopy/notAllowedByPath"), "");
                    throw HttpResponseException.CreateValidationErrorResponse(notificationModel);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Validates the composition and adds errors to the model state if any are found then throws an error response if there are errors
        /// </summary>
        /// <param name="contentTypeSave"></param>
        /// <param name="composition"></param>
        /// <returns></returns>
        private HttpResponseException CreateCompositionValidationExceptionIfInvalid<TContentTypeSave, TPropertyType, TContentTypeDisplay>(TContentTypeSave contentTypeSave, TContentType composition)
            where TContentTypeSave : ContentTypeSave<TPropertyType>
            where TPropertyType : PropertyTypeBasic
            where TContentTypeDisplay : ContentTypeCompositionDisplay
        {
            var service = GetContentTypeService<TContentType>();
            var validateAttempt = service.ValidateComposition(composition);
            if (validateAttempt == false)
            {
                //if it's not successful then we need to return some model state for the property aliases that
                // are duplicated
                var invalidPropertyAliases = validateAttempt.Result.Distinct();
                AddCompositionValidationErrors<TContentTypeSave, TPropertyType>(contentTypeSave, invalidPropertyAliases);

                var display = UmbracoMapper.Map<TContentTypeDisplay>(composition);
                //map the 'save' data on top
                display = UmbracoMapper.Map(contentTypeSave, display);
                display.Errors = ModelState.ToErrorDictionary();
                throw HttpResponseException.CreateValidationErrorResponse(display);
            }
            return null;
        }

        public IContentTypeBaseService<T> GetContentTypeService<T>()
            where T : IContentTypeComposition
        {
            if (typeof(T).Implements<IContentType>())
                return ContentTypeService as IContentTypeBaseService<T>;
            if (typeof(T).Implements<IMediaType>())
                return MediaTypeService as IContentTypeBaseService<T>;
            if (typeof(T).Implements<IMemberType>())
                return MemberTypeService as IContentTypeBaseService<T>;
            throw new ArgumentException("Type " + typeof(T).FullName + " does not have a service.");
        }

        /// <summary>
        /// Adds errors to the model state if any invalid aliases are found then throws an error response if there are errors
        /// </summary>
        /// <param name="contentTypeSave"></param>
        /// <param name="invalidPropertyAliases"></param>
        /// <returns></returns>
        private void AddCompositionValidationErrors<TContentTypeSave, TPropertyType>(TContentTypeSave contentTypeSave, IEnumerable<string> invalidPropertyAliases)
            where TContentTypeSave : ContentTypeSave<TPropertyType>
            where TPropertyType : PropertyTypeBasic
        {
            foreach (var propertyAlias in invalidPropertyAliases)
            {
                //find the property relating to these
                var prop = contentTypeSave.Groups.SelectMany(x => x.Properties).Single(x => x.Alias == propertyAlias);
                var group = contentTypeSave.Groups.Single(x => x.Properties.Contains(prop));

                var key = string.Format("Groups[{0}].Properties[{1}].Alias", group.SortOrder, prop.SortOrder);
                ModelState.AddModelError(key, "Duplicate property aliases not allowed between compositions");
            }
        }

        /// <summary>
        /// If the exception is an InvalidCompositionException create a response exception to be thrown for validation errors
        /// </summary>
        /// <typeparam name="TContentTypeDisplay"></typeparam>
        /// <typeparam name="TContentTypeSave"></typeparam>
        /// <typeparam name="TPropertyType"></typeparam>
        /// <param name="ex"></param>
        /// <param name="contentTypeSave"></param>
        /// <param name="ct"></param>
        /// <param name="ctId"></param>
        /// <returns></returns>
        private HttpResponseException CreateInvalidCompositionResponseException<TContentTypeDisplay, TContentTypeSave, TPropertyType>(
            Exception ex, TContentTypeSave contentTypeSave, TContentType ct, int ctId)
            where TContentTypeDisplay : ContentTypeCompositionDisplay
            where TContentTypeSave : ContentTypeSave<TPropertyType>
            where TPropertyType : PropertyTypeBasic
        {
            InvalidCompositionException invalidCompositionException = null;
            if (ex is InvalidCompositionException)
            {
                invalidCompositionException = (InvalidCompositionException)ex;
            }
            else if (ex.InnerException is InvalidCompositionException)
            {
                invalidCompositionException = (InvalidCompositionException)ex.InnerException;
            }
            if (invalidCompositionException != null)
            {
                AddCompositionValidationErrors<TContentTypeSave, TPropertyType>(contentTypeSave, invalidCompositionException.PropertyTypeAliases);
                return CreateModelStateValidationException<TContentTypeSave, TContentTypeDisplay>(ctId, contentTypeSave, ct);
            }
            return null;
        }

        /// <summary>
        /// Used to throw the ModelState validation results when the ModelState is invalid
        /// </summary>
        /// <typeparam name="TContentTypeDisplay"></typeparam>
        /// <typeparam name="TContentTypeSave"></typeparam>
        /// <param name="ctId"></param>
        /// <param name="contentTypeSave"></param>
        /// <param name="ct"></param>
        private HttpResponseException CreateModelStateValidationException<TContentTypeSave, TContentTypeDisplay>(int ctId, TContentTypeSave contentTypeSave, TContentType ct)
            where TContentTypeDisplay : ContentTypeCompositionDisplay
            where TContentTypeSave : ContentTypeSave
        {
            TContentTypeDisplay forDisplay;
            if (ctId > 0)
            {
                //Required data is invalid so we cannot continue
                forDisplay = UmbracoMapper.Map<TContentTypeDisplay>(ct);
                //map the 'save' data on top
                forDisplay = UmbracoMapper.Map(contentTypeSave, forDisplay);
            }
            else
            {
                //map the 'save' data to display
                forDisplay = UmbracoMapper.Map<TContentTypeDisplay>(contentTypeSave);
            }

            forDisplay.Errors = ModelState.ToErrorDictionary();
            return HttpResponseException.CreateValidationErrorResponse(forDisplay);
        }
    }
}