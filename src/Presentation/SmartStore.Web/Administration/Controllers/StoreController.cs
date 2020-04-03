﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Web.Mvc;
using SmartStore.Admin.Models.Store;
using SmartStore.Admin.Models.Stores;
using SmartStore.Core.Domain.Stores;
using SmartStore.Core.Security;
using SmartStore.Services.Catalog;
using SmartStore.Services.Customers;
using SmartStore.Services.Directory;
using SmartStore.Services.Media;
using SmartStore.Services.Orders;
using SmartStore.Web.Framework.Controllers;
using SmartStore.Web.Framework.Filters;
using SmartStore.Web.Framework.Security;
using Telerik.Web.Mvc;

namespace SmartStore.Admin.Controllers
{
    [AdminAuthorize]
    public partial class StoreController : AdminControllerBase
    {
        private readonly ICurrencyService _currencyService;
        private readonly IProductService _productService;
        private readonly IProductAttributeService _productAttributeService;
        private readonly ICategoryService _categoryService;
        private readonly IManufacturerService _manufacturerService;
        private readonly ICustomerService _customerService;
        private readonly IOrderService _orderService;
        private readonly IPictureService _pictureService;
        private readonly IShoppingCartService _shoppingCartService;

        public StoreController(
            ICurrencyService currencyService,
            IProductService productService,
            IProductAttributeService productAttributeService,
            ICategoryService categoryService,
            IManufacturerService manufacturerService,
            ICustomerService customerService,
            IOrderService orderService,
            IPictureService pictureService,
            IShoppingCartService shoppingCartService)
        {
            _currencyService = currencyService;
            _productService = productService;
            _productAttributeService = productAttributeService;
            _categoryService = categoryService;
            _manufacturerService = manufacturerService;
            _customerService = customerService;
            _categoryService = categoryService;
            _orderService = orderService;
            _pictureService = pictureService;
            _shoppingCartService = shoppingCartService;
        }

        private void PrepareStoreModel(StoreModel model, Store store)
        {
            model.AvailableCurrencies = _currencyService.GetAllCurrencies(false, store == null ? 0 : store.Id)
                .Select(x => new SelectListItem
                {
                    Text = x.Name,
                    Value = x.Id.ToString()
                })
                .ToList();
        }

        // Ajax.
        public ActionResult AllStores(string label, string selectedIds)
        {
            var stores = Services.StoreService.GetAllStores();
            var ids = selectedIds.ToIntArray();

            if (label.HasValue())
            {
                stores.Insert(0, new Store { Name = label, Id = 0 });
            }

            var list =
                from m in stores
                select new
                {
                    id = m.Id.ToString(),
                    text = m.Name,
                    selected = ids.Contains(m.Id)
                };

            return new JsonResult { Data = list.ToList(), JsonRequestBehavior = JsonRequestBehavior.AllowGet };
        }

        #region list

        [Permission(Permissions.Configuration.Store.Read)]
        public ActionResult List()
        {
            return View();
        }

        [HttpPost, GridAction(EnableCustomBinding = true)]
        [Permission(Permissions.Configuration.Store.Read)]
        public ActionResult List(GridCommand command)
        {
            var gridModel = new GridModel<StoreModel>();

            var storeModels = Services.StoreService.GetAllStores()
                .Select(x =>
                {
                    var model = x.ToModel();

                    PrepareStoreModel(model, x);

                    model.Hosts = model.Hosts.EmptyNull().Replace(",", "<br />");

                    return model;
                })
                .ToList();

            gridModel.Data = storeModels;
            gridModel.Total = storeModels.Count();

            return new JsonResult
            {
                Data = gridModel
            };
        }

        [Permission(Permissions.Configuration.Store.Create)]
        public ActionResult Create()
        {
            var model = new StoreModel();
            PrepareStoreModel(model, null);

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        [Permission(Permissions.Configuration.Store.Create)]
        public ActionResult Create(StoreModel model, bool continueEditing)
        {
            if (ModelState.IsValid)
            {
                var store = model.ToEntity();

                // Ensure we have "/" at the end.
                store.Url = store.Url.EnsureEndsWith("/");
                Services.StoreService.InsertStore(store);

                NotifySuccess(T("Admin.Configuration.Stores.Added"));
                return continueEditing ? RedirectToAction("Edit", new { id = store.Id }) : RedirectToAction("List");
            }

            PrepareStoreModel(model, null);
            return View(model);
        }

        [Permission(Permissions.Configuration.Store.Read)]
        public ActionResult Edit(int id)
        {
            var store = Services.StoreService.GetStoreById(id);
            if (store == null)
            {
                return RedirectToAction("List");
            }

            var model = store.ToModel();
            PrepareStoreModel(model, store);

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        [FormValueRequired("save", "save-continue")]
        [Permission(Permissions.Configuration.Store.Update)]
        public ActionResult Edit(StoreModel model, bool continueEditing)
        {
            var store = Services.StoreService.GetStoreById(model.Id);
            if (store == null)
            {
                return RedirectToAction("List");
            }

            if (ModelState.IsValid)
            {
                store = model.ToEntity(store);

                // Ensure we have "/" at the end.
                store.Url = store.Url.EnsureEndsWith("/");
                Services.StoreService.UpdateStore(store);

                NotifySuccess(T("Admin.Configuration.Stores.Updated"));
                return continueEditing ? RedirectToAction("Edit", new { id = store.Id }) : RedirectToAction("List");
            }

            PrepareStoreModel(model, store);
            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Store.Delete)]
        public ActionResult Delete(int id)
        {
            var store = Services.StoreService.GetStoreById(id);
            if (store == null)
            {
                return RedirectToAction("List");
            }

            try
            {
                Services.StoreService.DeleteStore(store);

                // When we delete a store we should also ensure that all "per store" settings will also be deleted.
                var settingsToDelete = Services.Settings
                    .GetAllSettings()
                    .Where(s => s.StoreId == id)
                    .ToList();

                settingsToDelete.ForEach(x => Services.Settings.DeleteSetting(x));

                // When we had two stores and now have only one store, we also should delete all "per store" settings.
                var allStores = Services.StoreService.GetAllStores();

                if (allStores.Count == 1)
                {
                    settingsToDelete = Services.Settings
                        .GetAllSettings()
                        .Where(s => s.StoreId == allStores[0].Id)
                        .ToList();

                    settingsToDelete.ForEach(x => Services.Settings.DeleteSetting(x));
                }

                NotifySuccess(T("Admin.Configuration.Stores.Deleted"));
                return RedirectToAction("List");
            }
            catch (Exception ex)
            {
                NotifyError(ex);
            }

            return RedirectToAction("Edit", new { id = store.Id });
        }

        #endregion

        [Permission(Permissions.Customer.Read, false)]
        public ActionResult StoreDashboardReport()
        {
            var watch = new Stopwatch();
            watch.Start();

            var allOrders = _orderService.GetOrders(0, 0, null, null, null, null, null, null, null);
            var model = new StoreDashboardReportModel
            {
                ProductsCount = _productService.GetAllProducts().TotalCount.ToString("D"),
                CategoriesCount = _categoryService.GetAllCategories().TotalCount.ToString("D"),
                ManufacturersCount = _manufacturerService.GetAllManufacturers().Count.ToString("D"),
                AttributesCount = _productAttributeService.GetAllProductAttributes(0, int.MaxValue).TotalCount.ToString("D"),
                AttributeCombinationsCount = _productService.GetAllProductVariants().TotalCount.ToString("D"),
                MediaCount = _pictureService.GetPictures(0, int.MaxValue).TotalCount.ToString("D"),
                CustomersCount = _customerService.SearchCustomers(
                    new CustomerSearchQuery()
                    {
                        Deleted = false,
                        CustomerRoleIds = new int[] { 3 }
                    }
                ).TotalCount.ToString("D"),
                OrdersCount = allOrders.Count().ToString("D"),
                Sales = allOrders.Sum(x => x.OrderTotal).ToString("C0"),
                OnlineCustomersCount = _customerService.GetOnlineCustomers(DateTime.UtcNow.AddMinutes(-15), null, 0, int.MaxValue).TotalCount.ToString("D"),
                CartsValue = _shoppingCartService.GetAllOpenCartSubTotal().ToString("C0"),
                WishlistsValue = _shoppingCartService.GetAllOpenWishlistSubTotal().ToString("C0")
            };

            watch.Stop();
            Debug.WriteLine("StoreDashboardReport >>> " + watch.ElapsedMilliseconds);

            return PartialView(model);
        }
    }
}
